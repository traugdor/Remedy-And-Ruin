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

        private static readonly TimeSpan CalendarPollInterval = TimeSpan.FromSeconds(10);

        private readonly Func<double> getTotalHours;
        private readonly ManualResetEventSlim saveRequested = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim forceStopRequested = new ManualResetEventSlim(false);

        public EffectTimerThread(
            Guid guid, EffectBucket bucket, string cluster, float effectMult, float effectOnset,
            double startTotalHours, double endTotalHours, Func<double> getTotalHours,
            string secondaryEffectType, float? secondaryEffectMult,
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

        public void ApplyPoisonEffect(string EffectType, float EffectMult, float EffectOnset, string GUID, double start, double TimeSpanOrStop, string secondaryEffectType = null, float? secondaryEffectMult = null)
            => ApplyEffect(EffectBucket.Poison, EffectType, EffectMult, EffectOnset, GUID, start, TimeSpanOrStop, secondaryEffectType, secondaryEffectMult);

        public void ApplyIllnessEffect(string EffectType, float EffectMult, string GUID, double start, double TimeSpanOrStop, string secondaryEffectType = null, float? secondaryEffectMult = null)
            => ApplyEffect(EffectBucket.Illness, EffectType, EffectMult, 0f, GUID, start, TimeSpanOrStop, secondaryEffectType, secondaryEffectMult);

        public void ApplyPotionEffect(string EffectType, float EffectMult, string GUID, double start, double TimeSpanOrStop, string secondaryEffectType = null, float? secondaryEffectMult = null)
            => ApplyEffect(EffectBucket.Potion, EffectType, EffectMult, 0f, GUID, start, TimeSpanOrStop, secondaryEffectType, secondaryEffectMult);

        // start = calendar TotalHours when this instance began; TimeSpanOrStop = absolute
        // calendar TotalHours it ends at. Both already computed by the caller - this method
        // just hands them straight to the timer.
        private void ApplyEffect(EffectBucket bucket, string cluster, float effectMult, float effectOnset, string guid, double start, double endTotalHours, string secondaryEffectType, float? secondaryEffectMult)
        {
            /*
             * PLACEHOLDER
             * Apply this effect - and, if present, the secondary effect - to entity.Stats here.
             * Use guid as the stat source/code so this instance can be independently blended
             * against any other active effect on the same stat category, and independently
             * removed later without disturbing the others.
             */

            var thread = new EffectTimerThread(
                Guid.Parse(guid), bucket, cluster, effectMult, effectOnset,
                start, endTotalHours, () => entity.World.Calendar.TotalHours,
                secondaryEffectType, secondaryEffectMult,
                onSaveReport: OnSaveReport,
                onNaturalEnd: OnNaturalEnd,
                onForcedEnd: OnForcedEnd);

            threads[thread.Guid] = thread;
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
            pendingForcedEndGuids?.Add(t.Guid);
            pendingForcedEndCountdown?.Signal();
        }

        private void OnNaturalEnd(EffectTimerThread t)
        {
            threads.TryRemove(t.Guid, out _);
            RemoveGuidsFromWatchedAttributes(new[] { t.Guid });

            /*
             * PLACEHOLDER
             * Natural expiry only - award tolerance progression etc. here once that logic
             * exists. Forced end (OnForcedEnd, above) must never take this path: effects lost
             * to an antidote or death do not count towards tolerance.
             */
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
