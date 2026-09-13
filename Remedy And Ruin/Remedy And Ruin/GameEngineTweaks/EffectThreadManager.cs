using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    public enum EffectBucket
    {
        Poison,
        Illness,
        Potion
    }

    public readonly struct StatModifier
    {
        public readonly string Category;
        public readonly float Value;
        public readonly bool Persistent;

        public StatModifier(string category, float value, bool persistent = false)
        {
            Category = category;
            Value = value;
            Persistent = persistent;
        }
    }

    internal sealed class ActiveEffectReport
    {
        public Guid Guid;
        public EffectBucket Bucket;
        public string Cluster;
        public float EffectMult;
        public float EffectOnset;
        public double StartTotalHours;
        public double EndTotalHours;
        public string SecondaryEffectType;
        public float? SecondaryEffectMult;
    }

    /// <summary>
    /// One real background thread per active effect, acting purely as a timer against the game
    /// calendar. EndTotalHours is an absolute entity.World.Calendar.TotalHours value - the
    /// caller (EntityBehaviorRemedyEffects) is responsible for computing it correctly for
    /// whichever mode a given effect uses; this class only ever polls the live calendar against
    /// that one fixed number.
    ///
    /// This does not by itself make an effect pause while a specific player is offline - the
    /// entity (and this thread) stays resident and this loop keeps polling for as long as the
    /// entity/behavior is loaded, independent of that one player's connection state. Per-player
    /// "does not advance while offline" behavior is a caller-side concern (recomputing
    /// EndTotalHours from a persisted remaining-hours value at each login) - this class has no
    /// opinion on it.
    /// </summary>
    internal sealed class EffectTimerThread
    {
        public Guid Guid { get; }
        public EffectBucket Bucket { get; }
        public string Cluster { get; }
        public float EffectMult { get; }
        public float EffectOnset { get; }
        public double StartTotalHours { get; }
        public double EndTotalHours { get; }
        public string SecondaryEffectType { get; }
        public float? SecondaryEffectMult { get; }
        public IReadOnlyList<StatModifier> StatModifiers { get; }

        private static readonly TimeSpan CalendarPollInterval = TimeSpan.FromSeconds(10);

        private readonly Func<double> getTotalHours;
        private readonly ManualResetEventSlim saveRequested = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim forceStopRequested = new ManualResetEventSlim(false);

        public EffectTimerThread(
            Guid guid, EffectBucket bucket, string cluster, float effectMult, float effectOnset,
            double startTotalHours, double endTotalHours, Func<double> getTotalHours,
            string secondaryEffectType, float? secondaryEffectMult,
            IReadOnlyList<StatModifier> statModifiers,
            Action<EffectTimerThread> onSaveReport,
            Action<EffectTimerThread> onNaturalEnd,
            Action<EffectTimerThread> onForcedEnd)
        {
            Guid = guid;
            Bucket = bucket;
            Cluster = cluster;
            EffectMult = effectMult;
            EffectOnset = effectOnset;
            StartTotalHours = startTotalHours;
            EndTotalHours = endTotalHours;
            SecondaryEffectType = secondaryEffectType;
            SecondaryEffectMult = secondaryEffectMult;
            StatModifiers = statModifiers;
            this.getTotalHours = getTotalHours;

            System.Threading.Tasks.Task.Run(() => Run(onSaveReport, onNaturalEnd, onForcedEnd));
        }

        public void RequestSaveReport() => saveRequested.Set();
        public void RequestForcedEnd() => forceStopRequested.Set();

        private void Run(Action<EffectTimerThread> onSaveReport, Action<EffectTimerThread> onNaturalEnd, Action<EffectTimerThread> onForcedEnd)
        {
            WaitHandle[] handles = { saveRequested.WaitHandle, forceStopRequested.WaitHandle };
            try
            {
                while (true)
                {
                    if (getTotalHours() >= EndTotalHours)
                    {
                        onNaturalEnd(this);
                        return;
                    }

                    int signalled = WaitHandle.WaitAny(handles, CalendarPollInterval);

                    if (signalled == 0) // save requested - report and keep running
                    {
                        onSaveReport(this);
                        saveRequested.Reset();
                        continue;
                    }
                    if (signalled == 1) // forced end requested
                    {
                        onForcedEnd(this);
                        return;
                    }
                    // WaitHandle.WaitTimeout -> loop back to re-check expiry / re-poll
                }
            }
            finally
            {
                saveRequested.Dispose();
                forceStopRequested.Dispose();
            }
        }
    }

    /// <summary>
    /// Owns every currently-running effect timer for one entity and is the sole writer of its
    /// remedyandruinEffects/rrpoisons, rrillness and rrpotions attributes - timer threads only
    /// ever report data to it and never touch WatchedAttributes themselves, which is what makes
    /// many concurrent timers safe without a lock on the tree itself. Exists for the lifetime of
    /// the owning EntityBehaviorRemedyEffects regardless of whether any effect is active.
    ///
    /// Writes back the same schema EntityBehaviorRemedyEffects.ApplyEffect/parseEffectsAndApply
    /// already read and wrote before any effect passed through here (effectname as
    /// "cluster|guid", cluster, isPoison, timestarted, timeleft, effectMultiplier,
    /// onsetMultiplier, toxicEffectMultiplier, toxicOnsetMultiplier) so a save/reload round-trip
    /// stays readable by that code. isConcentrated and toxicOnsetMultiplier have no source data
    /// in ApplyEffect's current parameters and are always written as false/0.
    /// </summary>
    public sealed class EffectThreadManager
    {
        private static readonly TimeSpan SaveReportTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ForcedEndTimeout = TimeSpan.FromSeconds(5);

        private readonly Entity entity;
        private readonly ConcurrentDictionary<Guid, EffectTimerThread> threads = new ConcurrentDictionary<Guid, EffectTimerThread>();

        private readonly object saveLock = new object();
        private ConcurrentBag<ActiveEffectReport> pendingSaveReports;
        private CountdownEvent pendingSaveCountdown;

        private readonly object forcedEndLock = new object();
        private ConcurrentBag<Guid> pendingForcedEndGuids;
        private CountdownEvent pendingForcedEndCountdown;

        public EffectThreadManager(Entity entity)
        {
            this.entity = entity;
        }

        //============== APPLYING EFFECTS ==============//

        public void ApplyPoisonEffect(string guid) => ApplyEffect(EffectBucket.Poison, guid);
        public void ApplyIllnessEffect(string guid) => ApplyEffect(EffectBucket.Illness, guid);
        public void ApplyPotionEffect(string guid) => ApplyEffect(EffectBucket.Potion, guid);

        // Reads the matching WatchedAttributes entry for guid, decides what entity.Stats
        // modifiers it applies (via DetermineStatModifiers), and spawns its timer thread.
        // effectMultiplier/onsetMultiplier are read as already-final, tolerance-discounted
        // values - this method never applies additional tolerance math to them.
        private void ApplyEffect(EffectBucket bucket, string guid)
        {
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return;

            TreeArrayAttribute source = bucket switch
            {
                EffectBucket.Poison => remedyEffects.RRPoisonEffects,
                EffectBucket.Illness => remedyEffects.RRIllnessEffects,
                EffectBucket.Potion => remedyEffects.RRPotionEffects,
                _ => null
            };
            if (source == null) return;

            TreeAttribute entry = source.value.FirstOrDefault(t => ExtractGuid(t) == guid);
            if (entry == null) return; // effect no longer present - nothing to apply

            string cluster = entry.GetString("cluster");
            float effectMult = entry.GetFloat("effectMultiplier");
            float effectOnset = entry.GetFloat("onsetMultiplier");
            float toxicEffectMultiplier = entry.GetFloat("toxicEffectMultiplier");
            double timeleft = entry.GetDouble("timeleft");

            // "now" is correct as this effect's start point the one time this method runs for a
            // given guid (parseEffectsAndApply's effectsApplied guard ensures that) - for a
            // brand-new effect this genuinely is when it started. Preserving the true original
            // start/end across a server restart is Plan 7's job once it adds persisted
            // reconnection state; this does not attempt that.
            double totalHoursNow = entity.World.Calendar.TotalHours;
            double startTotalHours = totalHoursNow;
            double endTotalHours = totalHoursNow + timeleft;

            IReadOnlyList<StatModifier> statModifiers = DetermineStatModifiers(cluster, effectMult, effectOnset, toxicEffectMultiplier);

            var thread = new EffectTimerThread(
                Guid.Parse(guid), bucket, cluster, effectMult, effectOnset,
                startTotalHours, endTotalHours, () => entity.World.Calendar.TotalHours,
                null, null, statModifiers,
                onSaveReport: OnSaveReport,
                onNaturalEnd: OnNaturalEnd,
                onForcedEnd: OnForcedEnd);

            threads[thread.Guid] = thread;

            if (statModifiers.Count > 0)
            {
                entity.Api.Event.EnqueueMainThreadTask(() => ApplyStatModifiers(thread), "rrEffectApplyStats");
            }
        }

        // PLACEHOLDER dispatch point - Plan 12 (poison clusters) and Plan 13 (remedy potions)
        // decide each cluster's real entity.Stats/DoT effect here, using the multipliers already
        // read off the WatchedAttributes entry in ApplyEffect.
        private static IReadOnlyList<StatModifier> DetermineStatModifiers(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                case "NOXIOUSPOISON":
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON":
                case "MINDPOISON":
                    break;
                default:
                    break;
            }
            return Array.Empty<StatModifier>();
        }

        private void ApplyStatModifiers(EffectTimerThread t)
        {
            foreach (StatModifier modifier in t.StatModifiers)
            {
                entity.Stats.Set(modifier.Category, t.Guid.ToString(), modifier.Value, modifier.Persistent);
            }
        }

        private void RemoveStatModifiers(EffectTimerThread t)
        {
            foreach (StatModifier modifier in t.StatModifiers)
            {
                entity.Stats.Remove(modifier.Category, t.Guid.ToString());
            }
        }

        //============== WORLD SAVE ==============//

        public bool HandleGameWorldSaving()
        {
            lock (saveLock)
            {
                List<EffectTimerThread> snapshot = threads.Values.ToList();
                if (snapshot.Count == 0) return true;

                pendingSaveReports = new ConcurrentBag<ActiveEffectReport>();
                pendingSaveCountdown = new CountdownEvent(snapshot.Count);

                foreach (EffectTimerThread t in snapshot)
                {
                    t.RequestSaveReport();
                }

                bool allReported = pendingSaveCountdown.Wait(SaveReportTimeout);

                WriteReportsToWatchedAttributes(pendingSaveReports);

                pendingSaveCountdown.Dispose();
                pendingSaveCountdown = null;
                pendingSaveReports = null;

                return allReported;
            }
        }

        //============== FORCED END (death / antidote) ==============//

        public bool HandleForcefulEnd()
        {
            lock (forcedEndLock)
            {
                // potions are not poisons/illnesses and are never force-ended by this path
                List<EffectTimerThread> snapshot = threads.Values
                    .Where(t => t.Bucket == EffectBucket.Poison || t.Bucket == EffectBucket.Illness)
                    .ToList();
                if (snapshot.Count == 0) return true;

                pendingForcedEndGuids = new ConcurrentBag<Guid>();
                pendingForcedEndCountdown = new CountdownEvent(snapshot.Count);

                foreach (EffectTimerThread t in snapshot)
                {
                    t.RequestForcedEnd();
                }

                bool allEnded = pendingForcedEndCountdown.Wait(ForcedEndTimeout);

                RemoveGuidsFromWatchedAttributes(pendingForcedEndGuids);

                pendingForcedEndCountdown.Dispose();
                pendingForcedEndCountdown = null;
                pendingForcedEndGuids = null;

                return allEnded;
            }
        }

        //============== THREAD CALLBACKS ==============//

        private void OnSaveReport(EffectTimerThread t)
        {
            pendingSaveReports?.Add(BuildReport(t));
            pendingSaveCountdown?.Signal();
        }

        private void OnForcedEnd(EffectTimerThread t)
        {
            threads.TryRemove(t.Guid, out _);
            entity.Api.Event.EnqueueMainThreadTask(() => RemoveStatModifiers(t), "rrEffectForcedEndStats");
            // Must stay synchronous and immediate, not nested inside the enqueued action above:
            // HandleForcefulEnd blocks the main thread on pendingForcedEndCountdown.Wait(...), so
            // gating this signal behind its own main-thread task would stall it for the full
            // timeout waiting on a task the main thread can't run until it stops waiting.
            pendingForcedEndGuids?.Add(t.Guid);
            pendingForcedEndCountdown?.Signal();
        }

        private void OnNaturalEnd(EffectTimerThread t)
        {
            threads.TryRemove(t.Guid, out _);
            entity.Api.Event.EnqueueMainThreadTask(() =>
            {
                RemoveStatModifiers(t);
                RemoveGuidsFromWatchedAttributes(new[] { t.Guid });

                /*
                 * PLACEHOLDER
                 * Natural expiry only - award tolerance progression here once that logic exists.
                 * Forced end (above) must never take this path: effects lost to an antidote or
                 * death do not count towards tolerance. Belongs in this same enqueued task,
                 * since it needs main-thread access to WatchedAttributes-backed tolerance
                 * counters.
                 */
            }, "rrEffectNaturalEnd");
        }

        private static ActiveEffectReport BuildReport(EffectTimerThread t) => new ActiveEffectReport
        {
            Guid = t.Guid,
            Bucket = t.Bucket,
            Cluster = t.Cluster,
            EffectMult = t.EffectMult,
            EffectOnset = t.EffectOnset,
            StartTotalHours = t.StartTotalHours,
            EndTotalHours = t.EndTotalHours,
            SecondaryEffectType = t.SecondaryEffectType,
            SecondaryEffectMult = t.SecondaryEffectMult
        };

        //============== WATCHEDATTRIBUTES I/O (single writer) ==============//

        private void WriteReportsToWatchedAttributes(IEnumerable<ActiveEffectReport> reports)
        {
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return;

            double totalHoursNow = entity.World.Calendar.TotalHours;

            var poisons = new List<TreeAttribute>();
            var illnesses = new List<TreeAttribute>();
            var potions = new List<TreeAttribute>();

            foreach (ActiveEffectReport report in reports)
            {
                TreeAttribute entry = BuildTreeAttribute(report, totalHoursNow);
                switch (report.Bucket)
                {
                    case EffectBucket.Poison: poisons.Add(entry); break;
                    case EffectBucket.Illness: illnesses.Add(entry); break;
                    case EffectBucket.Potion: potions.Add(entry); break;
                }
            }

            remedyEffects.RREffects["rrpoisons"] = new TreeArrayAttribute(poisons.ToArray());
            remedyEffects.RREffects["rrillness"] = new TreeArrayAttribute(illnesses.ToArray());
            remedyEffects.RREffects["rrpotions"] = new TreeArrayAttribute(potions.ToArray());
            remedyEffects.MarkDirty();
        }

        private void RemoveGuidsFromWatchedAttributes(IEnumerable<Guid> guids)
        {
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return;

            var guidSet = new HashSet<string>(guids.Select(g => g.ToString()));
            if (guidSet.Count == 0) return;

            remedyEffects.RREffects["rrpoisons"] = new TreeArrayAttribute(FilterOut(remedyEffects.RRPoisonEffects, guidSet));
            remedyEffects.RREffects["rrillness"] = new TreeArrayAttribute(FilterOut(remedyEffects.RRIllnessEffects, guidSet));
            remedyEffects.RREffects["rrpotions"] = new TreeArrayAttribute(FilterOut(remedyEffects.RRPotionEffects, guidSet));
            remedyEffects.MarkDirty();
        }

        private static TreeAttribute[] FilterOut(TreeArrayAttribute source, HashSet<string> guidsToRemove)
        {
            return source.value.Where(t => !guidsToRemove.Contains(ExtractGuid(t))).ToArray();
        }

        private static string ExtractGuid(TreeAttribute entry)
        {
            string effectname = entry.GetString("effectname");
            int separator = effectname?.IndexOf('|') ?? -1;
            return separator >= 0 ? effectname.Substring(separator + 1) : effectname;
        }

        private static TreeAttribute BuildTreeAttribute(ActiveEffectReport report, double totalHoursNow)
        {
            var t = new TreeAttribute();
            t.SetString("effectname", report.Cluster + "|" + report.Guid);
            t.SetString("cluster", report.Cluster);
            t.SetBool("isPoison", report.Bucket == EffectBucket.Poison);
            t.SetBool("isConcentrated", false); // no source data in ApplyEffect's current inputs
            t.SetDouble("timestarted", report.StartTotalHours);
            // remaining game-hours as of this save - read back in as "timeleft" to rebuild
            // EndTotalHours fresh (totalHoursNow-at-that-point + timeleft) on the next login
            t.SetDouble("timeleft", Math.Max(0.0, report.EndTotalHours - totalHoursNow));
            t.SetFloat("effectMultiplier", report.EffectMult);
            t.SetFloat("onsetMultiplier", report.EffectOnset);
            t.SetFloat("toxicEffectMultiplier", report.SecondaryEffectMult ?? 0f);
            t.SetFloat("toxicOnsetMultiplier", 0f); // no source data in ApplyEffect's current inputs
            return t;
        }
    }
}
