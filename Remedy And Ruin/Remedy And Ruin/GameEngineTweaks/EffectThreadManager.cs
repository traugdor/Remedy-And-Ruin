using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

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

    public readonly struct DoTSpec
    {
        public readonly EnumDamageSource DamageSource;
        public readonly EnumDamageType DamageType;
        public readonly int DamageTier;
        public readonly float TotalDamage;
        public readonly TimeSpan TotalTime;
        public readonly int TicksNumber;

        public DoTSpec(EnumDamageSource damageSource, EnumDamageType damageType, int damageTier, float totalDamage, TimeSpan totalTime, int ticksNumber)
        {
            DamageSource = damageSource;
            DamageType = damageType;
            DamageTier = damageTier;
            TotalDamage = totalDamage;
            TotalTime = totalTime;
            TicksNumber = ticksNumber;
        }
    }

    /// <summary>
    /// A flat, persistent adjustment to an entity's max health via
    /// EntityBehaviorHealth.SetMaxHealthModifiers - additively summed with every other keyed
    /// modifier into MaxHealth. No Category/key field: EffectThreadManager always keys this by
    /// the owning effect's own Guid, exactly like StatModifier's entity.Stats key.
    /// </summary>
    public readonly struct MaxHealthModifier
    {
        public readonly float Value;
        public MaxHealthModifier(float value) { Value = value; }
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
        public double OnsetCompleteTotalHours;
        public string SecondaryEffectType;
        public float? SecondaryEffectMult;
        public float? SecondaryEffectOnsetMult;
        public bool EligibleForTolerance;
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
        public double OnsetCompleteTotalHours { get; }
        public string SecondaryEffectType { get; }
        public float? SecondaryEffectMult { get; }
        public float? SecondaryEffectOnsetMult { get; }

        // The effect's currently-active package - onset-phase values until the onset transition
        // fires (or full-phase values from the start, for a thread constructed already past its
        // own onset window). TransitionToFullPhase is the only thing that ever changes these.
        public IReadOnlyList<StatModifier> StatModifiers { get; private set; }
        public DoTSpec? DoT { get; private set; }
        public MaxHealthModifier? MaxHealthMod { get; private set; }

        // The full-phase package, held alongside the active one above so the onset-transition
        // callback knows what to switch to without having to re-derive it.
        public IReadOnlyList<StatModifier> FullStatModifiers { get; }
        public DoTSpec? FullDoT { get; }
        public MaxHealthModifier? FullMaxHealthModifier { get; }

        public bool EligibleForTolerance { get; }

        private static readonly TimeSpan CalendarPollInterval = TimeSpan.FromSeconds(10);

        private readonly Func<double> getTotalHours;
        private bool onsetTransitioned;
        private readonly ManualResetEventSlim saveRequested = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim forceStopRequested = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim disconnectRequested = new ManualResetEventSlim(false);

        public EffectTimerThread(
            Guid guid, EffectBucket bucket, string cluster, float effectMult, float effectOnset,
            double startTotalHours, double endTotalHours, double onsetCompleteTotalHours,
            Func<double> getTotalHours,
            string secondaryEffectType, float? secondaryEffectMult, float? secondaryEffectOnsetMult,
            IReadOnlyList<StatModifier> onsetStatModifiers, DoTSpec? onsetDoT,
            IReadOnlyList<StatModifier> fullStatModifiers, DoTSpec? fullDoT, MaxHealthModifier? fullMaxHealthModifier,
            bool eligibleForTolerance,
            Action<EffectTimerThread> onSaveReport,
            Action<EffectTimerThread> onNaturalEnd,
            Action<EffectTimerThread> onForcedEnd,
            Action<EffectTimerThread> onDisconnect,
            Action<EffectTimerThread> onOnsetComplete)
        {
            Guid = guid;
            Bucket = bucket;
            Cluster = cluster;
            EffectMult = effectMult;
            EffectOnset = effectOnset;
            StartTotalHours = startTotalHours;
            EndTotalHours = endTotalHours;
            OnsetCompleteTotalHours = onsetCompleteTotalHours;
            SecondaryEffectType = secondaryEffectType;
            SecondaryEffectMult = secondaryEffectMult;
            SecondaryEffectOnsetMult = secondaryEffectOnsetMult;
            FullStatModifiers = fullStatModifiers ?? Array.Empty<StatModifier>();
            FullDoT = fullDoT;
            FullMaxHealthModifier = fullMaxHealthModifier;
            EligibleForTolerance = eligibleForTolerance;
            this.getTotalHours = getTotalHours;

            // A thread resumed (save/reload, reconnect) past its own onset window already starts
            // directly in the full phase - the onset-phase symptom, if any, never re-applies.
            if (getTotalHours() >= OnsetCompleteTotalHours)
            {
                onsetTransitioned = true;
                StatModifiers = FullStatModifiers;
                DoT = FullDoT;
                MaxHealthMod = FullMaxHealthModifier;
            }
            else
            {
                StatModifiers = onsetStatModifiers ?? Array.Empty<StatModifier>();
                DoT = onsetDoT;
                MaxHealthMod = null; // max-health modifiers only ever apply once the full phase begins
            }

            System.Threading.Tasks.Task.Run(() => Run(onSaveReport, onNaturalEnd, onForcedEnd, onDisconnect, onOnsetComplete));
        }

        public void RequestSaveReport() => saveRequested.Set();
        public void RequestForcedEnd() => forceStopRequested.Set();
        public void RequestDisconnect() => disconnectRequested.Set();

        // Swaps the active StatModifiers/DoT/MaxHealthMod over to the full-phase package. The
        // caller is responsible for removing whatever the onset-phase package applied first -
        // this only updates which package RemoveStatModifiers/RemoveDoT/RemoveMaxHealthModifier
        // and their Apply* counterparts will see on their next call.
        internal void TransitionToFullPhase()
        {
            StatModifiers = FullStatModifiers;
            DoT = FullDoT;
            MaxHealthMod = FullMaxHealthModifier;
        }

        private void Run(Action<EffectTimerThread> onSaveReport, Action<EffectTimerThread> onNaturalEnd, Action<EffectTimerThread> onForcedEnd, Action<EffectTimerThread> onDisconnect, Action<EffectTimerThread> onOnsetComplete)
        {
            WaitHandle[] handles = { saveRequested.WaitHandle, forceStopRequested.WaitHandle, disconnectRequested.WaitHandle };
            try
            {
                while (true)
                {
                    if (getTotalHours() >= EndTotalHours)
                    {
                        onNaturalEnd(this);
                        return;
                    }

                    if (!onsetTransitioned && getTotalHours() >= OnsetCompleteTotalHours)
                    {
                        // Set before invoking the callback, not after - onOnsetComplete enqueues
                        // its own work onto the main thread and returns immediately, so the next
                        // loop iteration (possibly before that enqueued work has even run) must
                        // already see this as transitioned to avoid firing it twice.
                        onsetTransitioned = true;
                        onOnsetComplete(this);
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
                    if (signalled == 2) // disconnect requested - report final state and stop
                    {
                        onDisconnect(this);
                        return;
                    }
                    // WaitHandle.WaitTimeout -> loop back to re-check expiry / re-poll
                }
            }
            finally
            {
                saveRequested.Dispose();
                forceStopRequested.Dispose();
                disconnectRequested.Dispose();
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
    /// stays readable by that code. isConcentrated has no source data in ApplyEffect's current
    /// parameters and is always written as false.
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

        private readonly object disconnectLock = new object();
        private ConcurrentBag<ActiveEffectReport> pendingDisconnectReports;
        private CountdownEvent pendingDisconnectCountdown;
        private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);

        public EffectThreadManager(Entity entity)
        {
            this.entity = entity;
        }

        //============== APPLYING EFFECTS ==============//

        public void ApplyPoisonEffect(string guid) => ApplyEffect(EffectBucket.Poison, guid);
        public void ApplyIllnessEffect(string guid) => ApplyEffect(EffectBucket.Illness, guid);
        public void ApplyPotionEffect(string guid) => ApplyEffect(EffectBucket.Potion, guid);

        // Reads the matching WatchedAttributes entry for guid, decides what entity.Stats/DoT/
        // max-health modifiers it applies for the onset and full phases (via the Determine*
        // dispatch methods below), and spawns its timer thread. effectMultiplier/onsetMultiplier
        // are the exposure's own raw, undiscounted values - each cluster's Determine* case reads
        // its own tolerance counter off entity's EntityBehaviorRemedyEffects and applies the
        // (tolerance / 3) / 9.0f discount itself, since the discount math (and, for Toxic Poison,
        // the 15%-of-undiscounted-magnitude threshold) needs both figures at once.
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
            float toxicOnsetMultiplier = entry.GetFloat("toxicOnsetMultiplier");
            double timeleft = entry.GetDouble("timeleft");
            bool eligibleForTolerance = entry.GetBool("toleranceEligible");
            bool calendarAnchored = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;

            // "now" is correct as this effect's start point the one time this method runs for a
            // given guid (parseEffectsAndApply's effectsApplied guard ensures that) - for a
            // brand-new effect this genuinely is when it started. Preserving the true original
            // start/end across a server restart is Plan 7's job once it adds persisted
            // reconnection state; this does not attempt that.
            double totalHoursNow = entity.World.Calendar.TotalHours;
            double startTotalHours = totalHoursNow;
            double endTotalHours;
            if (calendarAnchored && entry.HasAttribute("absoluteEndTotalHours"))
            {
                endTotalHours = entry.GetDouble("absoluteEndTotalHours");
            }
            else
            {
                endTotalHours = totalHoursNow + timeleft;
            }
            double onsetCompleteTotalHours = ComputeOnsetCompleteTotalHours(entry, cluster, effectOnset, startTotalHours, totalHoursNow, calendarAnchored);

            IReadOnlyList<StatModifier> onsetStatModifiers = DetermineOnsetStatModifiers(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier);
            DoTSpec? onsetDot = DetermineOnsetDoTEffect(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier);
            IReadOnlyList<StatModifier> fullStatModifiers = DetermineFullStatModifiers(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier);
            DoTSpec? fullDot = DetermineFullDoTEffect(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier);
            MaxHealthModifier? fullMaxHealthModifier = DetermineFullMaxHealthModifier(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier);

            var thread = new EffectTimerThread(
                Guid.Parse(guid), bucket, cluster, effectMult, effectOnset,
                startTotalHours, endTotalHours, onsetCompleteTotalHours, () => entity.World.Calendar.TotalHours,
                null, toxicEffectMultiplier, toxicOnsetMultiplier,
                onsetStatModifiers, onsetDot,
                fullStatModifiers, fullDot, fullMaxHealthModifier,
                eligibleForTolerance,
                onSaveReport: OnSaveReport,
                onNaturalEnd: OnNaturalEnd,
                onForcedEnd: OnForcedEnd,
                onDisconnect: OnDisconnect,
                onOnsetComplete: OnOnsetComplete);

            threads[thread.Guid] = thread;

            if (thread.StatModifiers.Count > 0 || thread.DoT != null || thread.MaxHealthMod != null)
            {
                entity.Api.Event.EnqueueMainThreadTask(() =>
                {
                    ApplyStatModifiers(thread);
                    ApplyDoT(thread);
                    ApplyMaxHealthModifier(thread);
                }, "rrEffectApply");
            }
        }

        // Baseline onset-delay hours for this cluster at onsetMultiplier 1.0 - the design doc's
        // ingredient tables give onset as a multiplier, not raw hours, so each cluster supplies
        // its own baseline here once its effect logic exists. 0 means "no onset delay yet" (the
        // full-phase package applies immediately), the correct placeholder until a cluster's own
        // task fills in its real value.
        private static double BaselineOnsetHours(string cluster)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                    // 2h at onsetMultiplier 1.0 - the ingredient table's 0.8-1.0 onset range for
                    // Death Cap/Funeral Bell/Fool's Conecap gives a 1.6-2h real reaction window.
                    return 2.0;
                case "NOXIOUSPOISON":
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON":
                case "MINDPOISON":
                    return 0.0;
                default:
                    return 0.0;
            }
        }

        // Mirrors how endTotalHours is derived from timeleft/absoluteEndTotalHours just above:
        // a brand-new entry (no persisted onset progress yet) derives its onset window fresh from
        // this cluster's baseline; a reloaded/reconnected one resumes the same window it already
        // had, which is what lets an effect already past onset skip straight to the full phase.
        private static double ComputeOnsetCompleteTotalHours(TreeAttribute entry, string cluster, float effectOnset, double startTotalHours, double totalHoursNow, bool calendarAnchored)
        {
            if (calendarAnchored && entry.HasAttribute("absoluteOnsetCompleteTotalHours"))
            {
                return entry.GetDouble("absoluteOnsetCompleteTotalHours");
            }
            if (entry.HasAttribute("onsetTimeLeft"))
            {
                return totalHoursNow + entry.GetDouble("onsetTimeLeft");
            }
            return startTotalHours + BaselineOnsetHours(cluster) * effectOnset;
        }

        // Toxic Poison's tolerance-discounted magnitude: (tolerance / 3) is the tier reached
        // (0-9, integer division), each tier worth 1/9 of effectMult. Floored at 0 - full 9/9
        // tolerance discounts the whole exposure away regardless of its own magnitude.
        private float ToxicToleranceDiscountedEffect(float effectMult)
        {
            int tolerance = entity.GetBehavior<EntityBehaviorRemedyEffects>()?.toxicTolerance ?? 0;
            float discount = (float)(tolerance / 3) / 9.0f;
            return Math.Max(0f, effectMult - discount);
        }

        private static readonly StatModifier[] ToxicHealingDip = { new StatModifier("healingeffectivness", -0.15f) };

        // PLACEHOLDER dispatch point - each poison cluster decides its own onset-phase (early
        // warning) entity.Stats effect here, using the multipliers already read off the
        // WatchedAttributes entry in ApplyEffect. A cluster with no distinct onset symptom
        // returns Array.Empty<StatModifier>().
        private IReadOnlyList<StatModifier> DetermineOnsetStatModifiers(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                    return ToxicHealingDip;
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

        // PLACEHOLDER dispatch point - each poison cluster decides its own onset-phase DoT here,
        // if it has one. No cluster currently needs a DoT before its full effect kicks in.
        private DoTSpec? DetermineOnsetDoTEffect(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier)
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
            return null;
        }

        // PLACEHOLDER dispatch point - Plan 12 (poison clusters) and Plan 13 (remedy potions)
        // decide each cluster's real full-phase entity.Stats effect here, using the multipliers
        // already read off the WatchedAttributes entry in ApplyEffect. Applied once onset
        // completes (or immediately, for an effect constructed already past its onset window).
        private IReadOnlyList<StatModifier> DetermineFullStatModifiers(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                    // The healing dip carries over unchanged from the onset phase whether or not
                    // the liver-failure DoT below ends up starting - below the 15% threshold this
                    // dip is the exposure's entire effect, fading only when its own timer ends.
                    return ToxicHealingDip;
                case "NOXIOUSPOISON":
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON":
                case "MINDPOISON":
                    break;
                case "ANTIDOTEAFTERMATH":
                    return new StatModifier[]
                    {
                        new StatModifier("meleeWeaponsDamage", -0.15f),
                        new StatModifier("healingeffectivness", -0.15f)
                    };
                default:
                    break;
            }
            return Array.Empty<StatModifier>();
        }

        // PLACEHOLDER dispatch point - Plan 12 decides each cluster's real full-phase DoT effect
        // here, using the multipliers already read off the WatchedAttributes entry in ApplyEffect.
        private DoTSpec? DetermineFullDoTEffect(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                    // Liver failure only actually triggers once the tolerance-discounted effect
                    // reaches 15% of this exposure's own undiscounted magnitude - a percentage of
                    // that exposure's own dose, not of 1.0, so a weak dose and a strong dose cross
                    // out of DoT range at the same tolerance tier regardless of their raw strength.
                    if (ToxicToleranceDiscountedEffect(effectMult) >= 0.15f * effectMult)
                    {
                        return BuildEffectivelyForeverDoT(EnumDamageSource.Internal, EnumDamageType.Poison, damageTier: 0, damagePerSecond: 1.5f);
                    }
                    break;
                case "NOXIOUSPOISON":
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON":
                case "MINDPOISON":
                    /* PLACEHOLDER - Plan 12 decides whether this cluster needs a DoT at all. */
                    break;
                default:
                    /* Remedy potions don't use DoT. */
                    break;
            }
            return null;
        }

        // PLACEHOLDER dispatch point - Cardiac Poison's flat max-health hit (and any other
        // cluster's own max-health mechanic) is decided here, via
        // EntityBehaviorHealth.SetMaxHealthModifiers rather than entity.Stats (which cannot touch
        // max health at all). A cluster with no max-health mechanic returns null.
        private MaxHealthModifier? DetermineFullMaxHealthModifier(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier)
        {
            switch (cluster)
            {
                case "TOXICPOISON": // liver failure has no max-health component
                case "NOXIOUSPOISON":
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON":
                case "MINDPOISON":
                    break;
                default:
                    break;
            }
            return null;
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

        private void ApplyDoT(EffectTimerThread t)
        {
            if (t.DoT == null) return;
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            DoTSpec spec = t.DoT.Value;
            health.ApplyDoTEffect(spec.DamageSource, spec.DamageType, spec.DamageTier, spec.TotalDamage, spec.TotalTime, spec.TicksNumber, t.Guid.GetHashCode());
        }

        private void RemoveDoT(EffectTimerThread t)
        {
            if (t.DoT == null) return;
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            health.StopDoTEffect(t.Guid.GetHashCode());
        }

        private void ApplyMaxHealthModifier(EffectTimerThread t)
        {
            if (t.MaxHealthMod == null) return;
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            float value = t.MaxHealthMod.Value.Value;
            health.SetMaxHealthModifiers(t.Guid.ToString(), value);

            // SetMaxHealthModifiers only ever changes MaxHealth (EntityBehaviorHealth.cs
            // ~line 359-380) - the matching hit to current health has to land as its own damage
            // event, since the engine never lets current health silently follow a max-health drop.
            if (value < 0f)
            {
                entity.ReceiveDamage(new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = EnumDamageType.Poison,
                    IgnoreInvFrames = true
                }, -value);
            }
        }

        private void RemoveMaxHealthModifier(EffectTimerThread t)
        {
            if (t.MaxHealthMod == null) return;
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            // SetMaxHealthModifiers has no dedicated removal call and the underlying dictionary
            // never drops a key once set (EntityBehaviorHealth.cs ~line 359-380, confirmed against
            // VSDecompile) - overwriting this key's value with 0 is the only way to stop it
            // contributing to MaxHealth. This restores headroom only; it does not itself heal the
            // entity back up.
            health.SetMaxHealthModifiers(t.Guid.ToString(), 0f);
        }

        // The engine's own ApplyDoTEffect (EntityBehaviorHealth.cs ~line 451) has no infinite
        // mode - TickDuration/PreviousTickTime are compared against entity.World.ElapsedMilliseconds
        // (real elapsed time, not the game calendar), so a "never ends on its own" DoT has to be a
        // large but finite real-time span instead. 1000 real days is far beyond any realistic play
        // session; ending it early is fully supported by RequestForcedEnd()/RemoveDoT's existing
        // StopDoTEffect call, which the Antidote's full-cure path already uses for every active
        // poison instance.
        private static readonly TimeSpan EffectivelyForeverDoTDuration = TimeSpan.FromDays(1000);
        private const float EffectivelyForeverTickSeconds = 6f;

        // Builds a DoTSpec for a poison whose damage never tapers off or ends naturally, at an
        // exact damagePerSecond rate (TotalDamage/TicksNumber is chosen so ApplyDoTEffect's own
        // per-tick math reproduces that rate). Internal rather than private: Toxic Poison's
        // arrow-hit bonus DoT (Patch_ArrowPoisonDelivery) builds its own reduced-rate instance
        // from this same helper instead of duplicating the tick-math.
        internal static DoTSpec BuildEffectivelyForeverDoT(EnumDamageSource damageSource, EnumDamageType damageType, int damageTier, float damagePerSecond)
        {
            int ticksNumber = (int)(EffectivelyForeverDoTDuration.TotalSeconds / EffectivelyForeverTickSeconds);
            float damagePerTick = damagePerSecond * EffectivelyForeverTickSeconds;
            float totalDamage = damagePerTick * ticksNumber;
            return new DoTSpec(damageSource, damageType, damageTier, totalDamage, EffectivelyForeverDoTDuration, ticksNumber);
        }

        // Identifies the arrow-delivered secondary Toxic DoT (Patch_ArrowPoisonDelivery) to
        // EntityBehaviorHealth's ActiveDoTEffects list and to a poison-caused death's own
        // DamageSource, since ProcessDoTEffects rebuilds a bare DamageSource per tick carrying
        // only Source/Type/DamageTier - DamageTier is the only field that survives into
        // OnEntityDeath's damageSourceForDeath, so it doubles as this DoT's own identity there.
        // Kept separate from every other Toxic-caused damage (tier 0) so an animal's meat is only
        // suppressed when this specific bonus DoT lands the killing blow, not any other Toxic hit.
        internal const int ArrowBonusToxicDoTEffectType = -19342;
        internal const int ArrowBonusToxicDoTDamageTier = 1;

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

        //============== DISCONNECT (pause without removing) ==============//

        public bool HandleDisconnect()
        {
            lock (disconnectLock)
            {
                List<EffectTimerThread> snapshot = threads.Values.ToList();
                if (snapshot.Count == 0) return true;

                pendingDisconnectReports = new ConcurrentBag<ActiveEffectReport>();
                pendingDisconnectCountdown = new CountdownEvent(snapshot.Count);

                foreach (EffectTimerThread t in snapshot)
                {
                    t.RequestDisconnect();
                }

                bool allStopped = pendingDisconnectCountdown.Wait(DisconnectTimeout);

                WriteReportsToWatchedAttributes(pendingDisconnectReports);

                pendingDisconnectCountdown.Dispose();
                pendingDisconnectCountdown = null;
                pendingDisconnectReports = null;

                return allStopped;
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
            entity.Api.Event.EnqueueMainThreadTask(() =>
            {
                RemoveStatModifiers(t);
                RemoveDoT(t);
                RemoveMaxHealthModifier(t);

                if (t.Bucket == EffectBucket.Poison)
                {
                    DiscardPendingToleranceOnForcedEnd(t.Cluster);
                }
            }, "rrEffectForcedEndStats");
            // Must stay synchronous and immediate, not nested inside the enqueued action above:
            // HandleForcefulEnd blocks the main thread on pendingForcedEndCountdown.Wait(...), so
            // gating this signal behind its own main-thread task would stall it for the full
            // timeout waiting on a task the main thread can't run until it stops waiting.
            pendingForcedEndGuids?.Add(t.Guid);
            pendingForcedEndCountdown?.Signal();
        }

        private void OnDisconnect(EffectTimerThread t)
        {
            threads.TryRemove(t.Guid, out _);
            pendingDisconnectReports?.Add(BuildReport(t));
            pendingDisconnectCountdown?.Signal();
        }

        private void OnNaturalEnd(EffectTimerThread t)
        {
            threads.TryRemove(t.Guid, out _);
            entity.Api.Event.EnqueueMainThreadTask(() =>
            {
                RemoveStatModifiers(t);
                RemoveDoT(t);
                RemoveMaxHealthModifier(t);
                RemoveGuidsFromWatchedAttributes(new[] { t.Guid });

                if (t.Bucket == EffectBucket.Poison)
                {
                    ResolveToleranceForCluster(t.Cluster, becameEligibleNow: t.EligibleForTolerance);
                }
            }, "rrEffectNaturalEnd");
        }

        // Fires once EffectTimerThread's own onset window has elapsed - swaps the onset-phase
        // package out for the full-phase one it was constructed with. This is a phase change, not
        // an end: the thread keeps running its normal poll loop afterward and this callback never
        // touches WatchedAttributes or tolerance.
        private void OnOnsetComplete(EffectTimerThread t)
        {
            entity.Api.Event.EnqueueMainThreadTask(() =>
            {
                RemoveStatModifiers(t);
                RemoveDoT(t);
                t.TransitionToFullPhase();
                ApplyStatModifiers(t);
                ApplyDoT(t);
                ApplyMaxHealthModifier(t);
            }, "rrEffectOnsetComplete");
        }

        // Awards a held tolerance credit only once every active thread for this cluster has
        // ended naturally. At most one eligible exposure can exist per overlapping chain (later
        // exposures of the same cluster are never eligible), so a single pending flag per
        // cluster is sufficient. Only ever called from OnNaturalEnd - a forced end (Antidote)
        // never awards or preserves credit, see DiscardPendingToleranceOnForcedEnd below.
        private void ResolveToleranceForCluster(string cluster, bool becameEligibleNow)
        {
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return;

            if (becameEligibleNow)
            {
                remedyEffects.SetPendingToleranceCredit(cluster, true);
            }

            bool clusterStillActive = threads.Values.Any(other => other.Bucket == EffectBucket.Poison && other.Cluster == cluster);
            if (!clusterStillActive && remedyEffects.GetPendingToleranceCredit(cluster))
            {
                remedyEffects.RegisterSurvivedExposure(cluster);
                remedyEffects.SetPendingToleranceCredit(cluster, false);
            }
        }

        // A forced end (Antidote) never earns or releases tolerance credit for this cluster - it
        // unconditionally discards whatever is pending, even credit already banked by an
        // earlier, fully-resolved natural survival of a different instance in the same cluster.
        private void DiscardPendingToleranceOnForcedEnd(string cluster)
        {
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return;

            remedyEffects.SetPendingToleranceCredit(cluster, false);
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
            OnsetCompleteTotalHours = t.OnsetCompleteTotalHours,
            SecondaryEffectType = t.SecondaryEffectType,
            SecondaryEffectMult = t.SecondaryEffectMult,
            SecondaryEffectOnsetMult = t.SecondaryEffectOnsetMult,
            EligibleForTolerance = t.EligibleForTolerance
        };

        //============== WATCHEDATTRIBUTES I/O (single writer) ==============//

        private void WriteReportsToWatchedAttributes(IEnumerable<ActiveEffectReport> reports)
        {
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return;

            double totalHoursNow = entity.World.Calendar.TotalHours;
            bool calendarAnchored = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;

            var poisons = new List<TreeAttribute>();
            var illnesses = new List<TreeAttribute>();
            var potions = new List<TreeAttribute>();

            foreach (ActiveEffectReport report in reports)
            {
                TreeAttribute entry = BuildTreeAttribute(report, totalHoursNow, calendarAnchored);
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

        private static TreeAttribute BuildTreeAttribute(ActiveEffectReport report, double totalHoursNow, bool calendarAnchored)
        {
            var t = new TreeAttribute();
            t.SetString("effectname", report.Cluster + "|" + report.Guid);
            t.SetString("cluster", report.Cluster);
            t.SetBool("isPoison", report.Bucket == EffectBucket.Poison);
            t.SetBool("isConcentrated", false); // no source data in ApplyEffect's current inputs
            t.SetBool("toleranceEligible", report.EligibleForTolerance);
            t.SetDouble("timestarted", report.StartTotalHours);
            if (calendarAnchored)
            {
                // The original, never-recomputed absolute target. Read back directly at
                // reconstruction - if the calendar has already passed it, the effect is treated as
                // having expired naturally, exactly like a live expiry would.
                t.SetDouble("absoluteEndTotalHours", report.EndTotalHours);
                t.SetDouble("timeleft", 0.0); // unused in this mode; present only for schema consistency
            }
            else
            {
                // Remaining game-hours as of this write - read back in as "timeleft" to rebuild
                // EndTotalHours fresh (totalHoursNow-at-reconnect + timeleft) next time this fires.
                t.SetDouble("timeleft", Math.Max(0.0, report.EndTotalHours - totalHoursNow));
            }
            if (calendarAnchored)
            {
                // Mirrors absoluteEndTotalHours above - read back directly so an effect already
                // past its onset window at reconstruction resumes straight into the full phase.
                t.SetDouble("absoluteOnsetCompleteTotalHours", report.OnsetCompleteTotalHours);
                t.SetDouble("onsetTimeLeft", 0.0); // unused in this mode; present only for schema consistency
            }
            else
            {
                // Mirrors timeleft above, for the onset window instead of the effect's own end.
                t.SetDouble("onsetTimeLeft", Math.Max(0.0, report.OnsetCompleteTotalHours - totalHoursNow));
            }
            t.SetFloat("effectMultiplier", report.EffectMult);
            t.SetFloat("onsetMultiplier", report.EffectOnset);
            t.SetFloat("toxicEffectMultiplier", report.SecondaryEffectMult ?? 0f);
            t.SetFloat("toxicOnsetMultiplier", report.SecondaryEffectOnsetMult ?? 0f);
            return t;
        }
    }
}
