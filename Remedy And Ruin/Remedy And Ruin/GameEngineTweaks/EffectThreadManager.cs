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
        public int DoseNumber;
        public float LadderWeight;
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
        // Mutable, not just for the onset/full-phase transition's own bookkeeping: Cardiac
        // Poison's exertion-stacking (and anything reusing it, e.g. Neurotoxic's dose-3) extends
        // a live effect's own end time from a game-tick listener outside this class entirely -
        // see ExtendEndTotalHours. Read every poll iteration by Run() on this thread's own
        // background thread while written from the main thread; a torn read costs at most one
        // CalendarPollInterval cycle of staleness before the loop re-checks, which this poll
        // design already tolerates for its own 10-second granularity.
        public double EndTotalHours { get; private set; }
        public double OnsetCompleteTotalHours { get; }
        public string SecondaryEffectType { get; }
        public float? SecondaryEffectMult { get; }
        public float? SecondaryEffectOnsetMult { get; }

        // Neurotoxic Poison's own ladder bookkeeping (unused by every other cluster) - resolved
        // once by EntityBehaviorRemedyEffects.ApplyNeurotoxicLadder at exposure time and threaded
        // through unchanged for this thread's whole lifetime, the same way EffectMult is.
        public int DoseNumber { get; }
        public float LadderWeight { get; }

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
            int doseNumber, float ladderWeight,
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
            DoseNumber = doseNumber;
            LadderWeight = ladderWeight;
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

        // Extends this effect's own end time - Cardiac Poison's exertion-stacking watcher calls
        // this once per stack (see EffectThreadManager.StartCardiacExertionStacking) instead of
        // this class exposing a public setter, so a stack's duration extension and its paired
        // MaxHealthModifier application always land together at the call site.
        internal void ExtendEndTotalHours(double additionalHours)
        {
            EndTotalHours += additionalHours;
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

        // Full-phase side effects that live outside the StatModifier/DoT/MaxHealthModifier
        // pipeline (Noxious Poison's fever hold, psychedelic hold, and vomit-roll; Mind Poison's
        // eventual equivalent) - each is a bare game tick listener id, keyed by the owning
        // effect's Guid so ApplyClusterFullPhaseSideEffects/RemoveClusterFullPhaseSideEffects can
        // start and tear them down without the StatModifiers/DoT machinery knowing about them.
        private readonly ConcurrentDictionary<Guid, List<long>> clusterSideEffectListeners = new ConcurrentDictionary<Guid, List<long>>();

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
            int doseNumber = entry.GetInt("doseNumber", 1);
            float ladderWeight = entry.GetFloat("ladderWeight", 1f);
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

            IReadOnlyList<StatModifier> onsetStatModifiers = DetermineOnsetStatModifiers(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier, doseNumber, ladderWeight);
            DoTSpec? onsetDot = DetermineOnsetDoTEffect(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier, doseNumber, ladderWeight);
            IReadOnlyList<StatModifier> fullStatModifiers = DetermineFullStatModifiers(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier, doseNumber, ladderWeight);
            DoTSpec? fullDot = DetermineFullDoTEffect(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier, doseNumber, ladderWeight);
            MaxHealthModifier? fullMaxHealthModifier = DetermineFullMaxHealthModifier(cluster, effectMult, effectOnset, toxicEffectMultiplier, toxicOnsetMultiplier, doseNumber, ladderWeight);

            var thread = new EffectTimerThread(
                Guid.Parse(guid), bucket, cluster, effectMult, effectOnset,
                startTotalHours, endTotalHours, onsetCompleteTotalHours, () => entity.World.Calendar.TotalHours,
                null, toxicEffectMultiplier, toxicOnsetMultiplier,
                onsetStatModifiers, onsetDot,
                fullStatModifiers, fullDot, fullMaxHealthModifier,
                eligibleForTolerance,
                doseNumber, ladderWeight,
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

            // A thread constructed already past its own onset window starts directly in the full
            // phase (see EffectTimerThread's constructor) - its full-phase side effects need to
            // start immediately too, not wait for an OnOnsetComplete that will never fire.
            if (onsetCompleteTotalHours <= totalHoursNow)
            {
                entity.Api.Event.EnqueueMainThreadTask(() => ApplyClusterFullPhaseSideEffects(thread), "rrEffectApplySideEffects");
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
                    // The ingredient table's own onset column (0.2-0.5) belongs to the secondary
                    // Toxic sliver, not Noxious's own primary onset - no dedicated primary-onset
                    // baseline is given, so 1.5h is used for consistency with Toxic's 2h baseline.
                    return 1.5;
                case "CARDIACPOISON":
                    // No flower ingredient JSON yet sets remedyandruinEffect.cluster to
                    // CardiacPoison with its own onset data (confirmed by searching the shipped
                    // assets) - 1.5h is used for consistency with Noxious's own baseline, the
                    // same fallback the design calls for absent real ingredient data.
                    return 1.5;
                case "NEUROTOXICPOISON":
                    // No flower ingredient JSON sets its own onset data for this cluster either
                    // (confirmed by checking assets/remedyandruin/recipes/barrel/poison-neurotoxicpoison.json,
                    // which only carries brewing data, not effect timing) - 1.5h for consistency
                    // with Noxious/Cardiac's own fallback.
                    return 1.5;
                case "MINDPOISON":
                    // The ingredient table's own onset range (0.7-1.1) suggests a baseline around
                    // 1.5-2h - 2h is used for consistency with Toxic's own baseline.
                    return 2.0;
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

        // Same tiered discount as ToxicToleranceDiscountedEffect, read against noxiousTolerance -
        // drives Noxious Poison's fever/tripping/vomit-roll intensity uniformly.
        private float NoxiousToleranceDiscountedEffect(float effectMult)
        {
            int tolerance = entity.GetBehavior<EntityBehaviorRemedyEffects>()?.noxiousTolerance ?? 0;
            float discount = (float)(tolerance / 3) / 9.0f;
            return Math.Max(0f, effectMult - discount);
        }

        // Same tiered discount as ToxicToleranceDiscountedEffect, read against
        // neurotoxicTolerance - drives the dizziness sway's own intensity.
        private float NeurotoxicToleranceDiscountedEffect(float effectMult)
        {
            int tolerance = entity.GetBehavior<EntityBehaviorRemedyEffects>()?.neurotoxicTolerance ?? 0;
            float discount = (float)(tolerance / 3) / 9.0f;
            return Math.Max(0f, effectMult - discount);
        }

        // Same tiered discount as ToxicToleranceDiscountedEffect, read against brainrotTolerance -
        // the one severity value that drives Temporal Fog, the drunken sway, Hallucination's
        // spawn-family scaling, the psychedelic-trip intensity, and the doubled hunger rate.
        private float MindPoisonToleranceDiscountedEffect(float effectMult)
        {
            int tolerance = entity.GetBehavior<EntityBehaviorRemedyEffects>()?.brainrotTolerance ?? 0;
            float discount = (float)(tolerance / 3) / 9.0f;
            return Math.Max(0f, effectMult - discount);
        }

        private static readonly StatModifier[] ToxicHealingDip = { new StatModifier("healingeffectivness", -0.15f) };

        // PLACEHOLDER dispatch point - each poison cluster decides its own onset-phase (early
        // warning) entity.Stats effect here, using the multipliers already read off the
        // WatchedAttributes entry in ApplyEffect. A cluster with no distinct onset symptom
        // returns Array.Empty<StatModifier>().
        private IReadOnlyList<StatModifier> DetermineOnsetStatModifiers(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier, int doseNumber, float ladderWeight)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                    return ToxicHealingDip;
                case "NOXIOUSPOISON":
                    // No distinct early-warning symptom - the onset delay is a silent reaction
                    // window only, the full GI-irritation package below applies with no ramp-up.
                    break;
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON": // no distinct early-warning symptom, same as Noxious/Cardiac
                case "MINDPOISON":
                    break;
                default:
                    break;
            }
            return Array.Empty<StatModifier>();
        }

        // PLACEHOLDER dispatch point - each poison cluster decides its own onset-phase DoT here,
        // if it has one. No cluster currently needs a DoT before its full effect kicks in.
        private DoTSpec? DetermineOnsetDoTEffect(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier, int doseNumber, float ladderWeight)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                case "NOXIOUSPOISON": // no DoT at any phase - deliberately non-lethal
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON": // no DoT at any phase - the ladder is stat modifiers/max-health only
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
        private IReadOnlyList<StatModifier> DetermineFullStatModifiers(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier, int doseNumber, float ladderWeight)
        {
            switch (cluster)
            {
                case "TOXICPOISON":
                    // The healing dip carries over unchanged from the onset phase whether or not
                    // the liver-failure DoT below ends up starting - below the 15% threshold this
                    // dip is the exposure's entire effect, fading only when its own timer ends.
                    return ToxicHealingDip;
                case "NOXIOUSPOISON":
                    // Fever, psychedelic tripping, and the vomit-roll all apply outside the
                    // entity.Stats pipeline (see ApplyClusterFullPhaseSideEffects below) - this
                    // cluster has no entity.Stats-based component of its own.
                    break;
                case "CARDIACPOISON":
                    return CardiacFullStatModifiers;
                case "NEUROTOXICPOISON":
                    return DetermineNeurotoxicFullStatModifiers(doseNumber, ladderWeight);
                case "MINDPOISON":
                    return MindPoisonFullStatModifiers;
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
        private DoTSpec? DetermineFullDoTEffect(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier, int doseNumber, float ladderWeight)
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
                case "NOXIOUSPOISON": // deliberately non-lethal - no DoT at any point
                case "CARDIACPOISON":
                case "NEUROTOXICPOISON": // the ladder is stat modifiers/max-health only, no DoT
                case "MINDPOISON": // deliberately non-lethal - no DoT at any point
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
        private MaxHealthModifier? DetermineFullMaxHealthModifier(string cluster, float effectMult, float effectOnset, float toxicEffectMultiplier, float toxicOnsetMultiplier, int doseNumber, float ladderWeight)
        {
            switch (cluster)
            {
                case "TOXICPOISON": // liver failure has no max-health component
                case "NOXIOUSPOISON": // fever/tripping/vomiting has no max-health component
                    break;
                case "CARDIACPOISON":
                    return DetermineCardiacMaxHealthModifier();
                case "NEUROTOXICPOISON":
                    // Only a real dose 3 (a drunk dose, never an arrow's half-weight instance)
                    // carries Cardiac Poison's flat HP hit - see DetermineNeurotoxicFullStatModifiers's
                    // own doc comment for why ladderWeight gates this the same way.
                    if (doseNumber >= 3 && ladderWeight >= 1f) return new MaxHealthModifier(-CardiacFlatHealthHit);
                    break;
                case "MINDPOISON": // deliberately non-lethal - no max-health component
                    break;
                default:
                    break;
            }
            return null;
        }

        // Full (9/9) Cardiac tolerance waives the flat HP hit entirely - the one place tolerance
        // discounts this cluster's magnitude rather than just duration (02-design-overview.md
        // ~845-852). EntityBehaviorRemedyEffects.ApplyEffect already skips creating a Cardiac
        // exposure at all once cardiacTolerance reaches 27, so this branch is normally
        // unreachable; kept as a defensive second check rather than relying on that alone.
        private MaxHealthModifier? DetermineCardiacMaxHealthModifier()
        {
            int cardiacTolerance = entity.GetBehavior<EntityBehaviorRemedyEffects>()?.cardiacTolerance ?? 0;
            if (cardiacTolerance >= 27) return null;
            return new MaxHealthModifier(-CardiacFlatHealthHit);
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
            float healthBeforeClamp = health.Health;
            health.SetMaxHealthModifiers(t.Guid.ToString(), value);

            // SetMaxHealthModifiers's own UpdateMaxHealth (EntityBehaviorHealth.cs ~line 388-406)
            // clamps current Health down to the new, lower MaxHealth whenever the entity was
            // already at or above it - a player at full health has already absorbed some or all
            // of a negative modifier's hit through that clamp alone before any damage event fires.
            // Only apply the remaining, not-yet-clamped portion as explicit damage, or a full-HP
            // player takes the flat hit twice (once via the clamp, once via ReceiveDamage).
            if (value < 0f)
            {
                float clampedAlready = healthBeforeClamp - health.Health;
                float remainingDamage = -value - clampedAlready;
                if (remainingDamage > 0f)
                {
                    entity.ReceiveDamage(new DamageSource
                    {
                        Source = EnumDamageSource.Internal,
                        Type = EnumDamageType.Poison,
                        IgnoreInvFrames = true
                    }, remainingDamage);
                }
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

        // Noxious Poison's fever hold reaches ~2C above normal body temperature at full
        // (tolerance-discounted) strength - within the real fever range, well short of vanilla's
        // own 31-45C hard clamp in EntityBehaviorBodyTemperature.
        private const float NoxiousFeverDegreesAtFullStrength = 2.0f;

        // Matches vanilla's own strongest single-dose mushroom (Blue Meanie, psychedelic 2 -
        // see 02-design-overview.md's Mind Poison ingredient table) at full strength, scaling
        // down with tolerance discount the same way the fever and vomit-roll do.
        private const float NoxiousPsychedelicIntensityAtFullStrength = 2.0f;

        // Midpoint of the 30-60s full-strength vomit interval range (Decisions locked in, plan
        // 12) - StartRepeatingVomitRoll jitters around this and divides by the discounted
        // multiplier itself.
        private const double NoxiousVomitRollBaseIntervalSeconds = 45.0;

        // Cardiac Poison's flat current/max HP hit - full strength regardless of tolerance below
        // full (9/9) crossing (02-design-overview.md ~845-852, ~1407). Also the per-stack hit
        // exertion-stacking applies, and the package Task 5's Neurotoxic dose-3 layers on top of
        // its own ladder reuses this same value and mechanism unmodified.
        private const float CardiacFlatHealthHit = 5f;

        // -40% walkspeed, the same value Neurotoxic's own Weakness stage uses (Decisions locked
        // in, plan 12) - chosen for consistency across the two clusters' movement debuffs rather
        // than a separately-tuned number.
        //
        // miningSpeedMul is the only stat category the engine actually reads for tool-use speed
        // (CollectibleObject.GetMiningSpeed, confirmed against VSDecompile) - it only factors in
        // against Ore/Stone-material blocks in the engine's own default mining-speed calculation,
        // not universally against every tool interaction, since no broader "tool speed" stat
        // exists to register against. Matches the walkspeed debuff's own magnitude.
        private static readonly StatModifier[] CardiacFullStatModifiers =
        {
            new StatModifier("walkspeed", -0.40f),
            new StatModifier("miningSpeedMul", -0.40f)
        };

        // Doubled hunger rate (02-design-overview.md ~1445-1456) - hungerrate blends additively
        // onto the engine's own base 1.0 rate, so +1.0 here reads as exactly double.
        private static readonly StatModifier[] MindPoisonFullStatModifiers = { new StatModifier("hungerrate", 1.0f) };

        // Neurotoxic's Weakness/Paralysis stages use this bare walkspeed value alone, without
        // Cardiac's additional miningSpeedMul component - that only enters at dose 3, which reuses
        // CardiacFullStatModifiers in full instead of adding this on top of it.
        private static readonly StatModifier NeurotoxicWeaknessWalkspeed = new StatModifier("walkspeed", -0.40f);

        // Stand-in for Neurotoxic's "headache" symptom: neither vanilla nor this mod has an
        // existing headache stat to hook into (confirmed by searching both the decompiled source
        // and this mod's own code - Skull-Strain's "Concussion" is an unrelated head-injury
        // mechanic, not this). rangedWeaponsAcc is a real, engine-read stat
        // (BaseAimingAccuracy.Update, confirmed against VSDecompile) - a mild aim/concentration
        // penalty stands in for head pain with no dedicated stat of its own.
        private static readonly StatModifier NeurotoxicHeadacheAccuracy = new StatModifier("rangedWeaponsAcc", -0.15f);

        /// <summary>
        /// Neurotoxic's ladder: dose 1 is Weakness alone; dose 2 is a second, independent
        /// Weakness instance (Paralysis - the two overlapping -40% walkspeed modifiers sum via
        /// entity.Stats' own additive blending, no separate stat category needed); dose 3 swaps
        /// in Cardiac Poison's own full package instead of Weakness (reused verbatim, per Task
        /// 4's own instruction - it already carries its own walkspeed hit). Headache applies
        /// unconditionally at every stage. An arrow-hit's half-weight instance (ladderWeight < 1)
        /// always gets bare Weakness regardless of the dose number its cumulative ladder
        /// progress nominally reaches - only a drunk dose can trigger dose 3's Cardiac package.
        /// </summary>
        private IReadOnlyList<StatModifier> DetermineNeurotoxicFullStatModifiers(int doseNumber, float ladderWeight)
        {
            var modifiers = new List<StatModifier> { NeurotoxicHeadacheAccuracy };
            if (ladderWeight >= 1f && doseNumber >= 3)
            {
                modifiers.AddRange(CardiacFullStatModifiers);
            }
            else
            {
                modifiers.Add(NeurotoxicWeaknessWalkspeed);
            }
            return modifiers;
        }

        // Cardiac Poison's exertion-stacking cadence and per-stack cost (02-design-overview.md
        // ~1418-1429): roughly one stack per 10 continuous seconds of sprinting or tool use,
        // +1h duration and -5 current/max HP each, uncapped.
        private const double CardiacExertionStackIntervalSeconds = 10.0;
        private const double CardiacExertionStackDurationHours = 1.0;

        // Tracks each Cardiac-style effect's current exertion-stack count, keyed by the owning
        // effect's Guid - StartCardiacExertionStacking appends a "{guid}-stackN" MaxHealthModifier
        // per stack (distinct from the base "{guid}" key RemoveMaxHealthModifier already handles),
        // and RemoveCardiacExertionStacks reads this to know how many of those keys to zero out
        // when the effect ends.
        private readonly ConcurrentDictionary<Guid, int> cardiacExertionStackCounts = new ConcurrentDictionary<Guid, int>();

        // Full-phase side effects that live outside the StatModifier/DoT/MaxHealthModifier
        // pipeline - dispatches per cluster and records whatever listener ids it starts so
        // RemoveClusterFullPhaseSideEffects can tear them down symmetrically. A cluster with none
        // of these (every cluster but Noxious, for now) is a no-op.
        private void ApplyClusterFullPhaseSideEffects(EffectTimerThread t)
        {
            if (t.Bucket != EffectBucket.Poison) return;

            List<long> listeners;
            switch (t.Cluster)
            {
                case "NOXIOUSPOISON":
                    listeners = StartNoxiousFullPhaseSideEffects(t.EffectMult);
                    break;
                case "CARDIACPOISON":
                    listeners = StartCardiacFullPhaseSideEffects(t);
                    break;
                case "NEUROTOXICPOISON":
                    listeners = StartNeurotoxicFullPhaseSideEffects(t);
                    break;
                case "MINDPOISON":
                    listeners = StartMindPoisonFullPhaseSideEffects(t);
                    break;
                default:
                    return;
            }

            if (listeners.Count > 0)
            {
                clusterSideEffectListeners[t.Guid] = listeners;
            }
        }

        // Cardiac Poison's only full-phase side effect outside the StatModifier/MaxHealthModifier
        // pipeline: the exertion-stacking watcher. Kept as its own method (rather than inlined
        // into the switch above) so Task 5's Neurotoxic dose-3 - which layers Cardiac Poison's
        // whole package, this watcher included, on top of its own ladder - can call
        // StartCardiacExertionStacking(t) directly against its own EffectTimerThread instead of
        // duplicating the watcher.
        private List<long> StartCardiacFullPhaseSideEffects(EffectTimerThread t)
        {
            var listeners = new List<long>();
            long exertionListener = StartCardiacExertionStacking(t);
            if (exertionListener != 0L) listeners.Add(exertionListener);
            return listeners;
        }

        /// <summary>
        /// Cardiac Poison's exertion-stacking watcher (02-design-overview.md ~1418-1429): while
        /// the owning effect is active, continuous sprinting (Controls.Sprint) or active tool use
        /// (Controls.HandUse != EnumHandInteract.None - confirmed real against VSDecompile's
        /// EntityAgent.TryStopHandAction, which reads this same field to detect an in-progress
        /// hand action) adds one stack roughly every
        /// CardiacExertionStackIntervalSeconds - a per-stack cooldown that resets the instant
        /// exertion stops, not a free-running tick. Each stack extends the owning effect's own
        /// EndTotalHours by CardiacExertionStackDurationHours and applies another
        /// CardiacFlatHealthHit MaxHealthModifier under its own "{guid}-stackN" key (via
        /// RemoveCardiacExertionStacks at teardown), so stacks compound instead of overwriting
        /// each other. No cap - ordinary walking with no tool in use never stacks. Reusable as-is
        /// by any effect package that inherits Cardiac Poison's rules (Task 5's Neurotoxic
        /// dose-3): call this with that effect's own EffectTimerThread; it has no opinion on
        /// which cluster t.Cluster reports.
        /// </summary>
        internal long StartCardiacExertionStacking(EffectTimerThread t)
        {
            if (!(entity is EntityAgent agent)) return 0L;

            double exertingSeconds = 0.0;

            return entity.World.RegisterGameTickListener(dt =>
            {
                bool exerting = agent.Controls.Sprint || agent.Controls.HandUse != EnumHandInteract.None;
                if (!exerting)
                {
                    exertingSeconds = 0.0;
                    return;
                }

                exertingSeconds += dt;
                if (exertingSeconds < CardiacExertionStackIntervalSeconds) return;
                exertingSeconds = 0.0;

                int stackCount = cardiacExertionStackCounts.AddOrUpdate(t.Guid, 1, (_, c) => c + 1);
                t.ExtendEndTotalHours(CardiacExertionStackDurationHours);

                var health = entity.GetBehavior<EntityBehaviorHealth>();
                if (health == null) return;

                health.SetMaxHealthModifiers(t.Guid + "-stack" + stackCount, -CardiacFlatHealthHit);
                entity.ReceiveDamage(new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = EnumDamageType.Poison,
                    IgnoreInvFrames = true
                }, CardiacFlatHealthHit);
            }, 1000);
        }

        // Zeroes every per-stack MaxHealthModifier key StartCardiacExertionStacking applied for
        // this effect ("{guid}-stackN") - the base "{guid}" key from the initial flat hit is
        // handled separately by RemoveMaxHealthModifier. A no-op for any effect that never
        // started exertion-stacking.
        private void RemoveCardiacExertionStacks(Guid effectGuid)
        {
            if (!cardiacExertionStackCounts.TryRemove(effectGuid, out int stackCount) || stackCount == 0) return;

            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            for (int i = 1; i <= stackCount; i++)
            {
                health.SetMaxHealthModifiers(effectGuid + "-stack" + i, 0f);
            }
        }

        /// <summary>
        /// Neurotoxic Poison's own full-phase side effects: dizziness (a
        /// TemporalVignetteRenderer.DrunkWobbleStrength contribution, bridged to the client via
        /// EntityBehaviorRemedyEffects.StartDrunkWobbleContribution's synced WatchedAttributes
        /// float) at every stage, plus Cardiac Poison's exertion-stacking watcher once dose 3's
        /// real Cardiac package is active (not for an arrow-hit's half-weight instance, which
        /// never carries that package - see DetermineNeurotoxicFullStatModifiers).
        /// </summary>
        private List<long> StartNeurotoxicFullPhaseSideEffects(EffectTimerThread t)
        {
            var listeners = new List<long>();
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects != null)
            {
                long wobbleListener = remedyEffects.StartDrunkWobbleContribution(t.Guid, NeurotoxicToleranceDiscountedEffect(t.EffectMult));
                if (wobbleListener != 0L) listeners.Add(wobbleListener);
            }

            if (t.DoseNumber >= 3 && t.LadderWeight >= 1f)
            {
                long exertionListener = StartCardiacExertionStacking(t);
                if (exertionListener != 0L) listeners.Add(exertionListener);
            }

            return listeners;
        }

        private List<long> StartNoxiousFullPhaseSideEffects(float effectMult)
        {
            var listeners = new List<long>();
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return listeners;

            float discounted = NoxiousToleranceDiscountedEffect(effectMult);

            long feverListener = remedyEffects.StartFeverHold(NoxiousFeverDegreesAtFullStrength * discounted);
            if (feverListener != 0L) listeners.Add(feverListener);

            long psychedelicListener = remedyEffects.StartPsychedelicHold(NoxiousPsychedelicIntensityAtFullStrength * discounted);
            if (psychedelicListener != 0L) listeners.Add(psychedelicListener);

            long vomitListener = remedyEffects.StartRepeatingVomitRoll(NoxiousVomitRollBaseIntervalSeconds, discounted);
            if (vomitListener != 0L) listeners.Add(vomitListener);

            return listeners;
        }

        // Matches vanilla's own Blue Meanie (psychedelic 2, the Mind Poison table's own strongest
        // single-dose mushroom) at full strength, scaling down with tolerance discount the same
        // way Noxious's own psychedelic hold does.
        private const float MindPoisonPsychedelicIntensityAtFullStrength = 2.0f;

        // Within the design doc's own stated 15-25% range for this cluster's move-triggered
        // vomiting (02-design-overview.md ~1445-1456).
        private const float MindPoisonMoveVomitChance = 0.20f;

        // How often the move-vomit watcher polls EntityControls.TriesToMove for a fresh
        // rising edge - fast enough that a brief tap of a movement key is never missed between
        // polls, without polling every single engine tick.
        private const int MindPoisonMoveVomitPollMs = 200;

        /// <summary>
        /// Mind Poison's full-phase side effects (02-design-overview.md ~1445-1456): the
        /// tolerance-discounted effect multiplier is this cluster's one severity value, driving
        /// Temporal Fog's screen effect and the drunken camera sway together via
        /// EntityBehaviorRemedyEffects.StartMindPoisonSeverityContribution's synced
        /// WatchedAttributes bridge (the same mechanism Neurotoxic's own dizziness bridge uses),
        /// plus genuine psychedelic tripping (StartPsychedelicHold, reused verbatim from Noxious)
        /// and the move-triggered vomit watcher.
        /// </summary>
        private List<long> StartMindPoisonFullPhaseSideEffects(EffectTimerThread t)
        {
            var listeners = new List<long>();
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyEffects == null) return listeners;

            float discounted = MindPoisonToleranceDiscountedEffect(t.EffectMult);

            long severityListener = remedyEffects.StartMindPoisonSeverityContribution(t.Guid, discounted);
            if (severityListener != 0L) listeners.Add(severityListener);

            long psychedelicListener = remedyEffects.StartPsychedelicHold(MindPoisonPsychedelicIntensityAtFullStrength * discounted);
            if (psychedelicListener != 0L) listeners.Add(psychedelicListener);

            long vomitListener = StartMindPoisonMoveVomitWatcher(remedyEffects);
            if (vomitListener != 0L) listeners.Add(vomitListener);

            return listeners;
        }

        /// <summary>
        /// Rolls MindPoisonMoveVomitChance once per genuine movement attempt rather than
        /// continuously while a movement key is held - EntityControls.TriesToMove (confirmed real
        /// against VSDecompile: true whenever Forward/Backward/Left/Right is held, deliberately
        /// excluding Jump) is polled for a false-to-true rising edge, so holding a key down for
        /// several seconds counts as one attempt, not dozens.
        /// </summary>
        private long StartMindPoisonMoveVomitWatcher(EntityBehaviorRemedyEffects remedyEffects)
        {
            if (!(entity is EntityAgent agent)) return 0L;

            bool wasMoving = false;
            var rand = new Random();

            return entity.World.RegisterGameTickListener(dt =>
            {
                bool triesToMove = agent.Controls.TriesToMove;
                if (triesToMove && !wasMoving && rand.NextDouble() < MindPoisonMoveVomitChance)
                {
                    remedyEffects.TriggerVomit();
                }
                wasMoving = triesToMove;
            }, MindPoisonMoveVomitPollMs);
        }

        private void RemoveClusterFullPhaseSideEffects(Guid effectGuid)
        {
            if (clusterSideEffectListeners.TryRemove(effectGuid, out List<long> listeners))
            {
                foreach (long id in listeners)
                {
                    entity.World.UnregisterGameTickListener(id);
                }
            }

            RemoveCardiacExertionStacks(effectGuid);
            var remedyEffects = entity.GetBehavior<EntityBehaviorRemedyEffects>();
            remedyEffects?.StopDrunkWobbleContribution(effectGuid);
            remedyEffects?.StopMindPoisonSeverityContribution(effectGuid);
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
                RemoveClusterFullPhaseSideEffects(t.Guid);

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
                RemoveClusterFullPhaseSideEffects(t.Guid);
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
                ApplyClusterFullPhaseSideEffects(t);
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
            EligibleForTolerance = t.EligibleForTolerance,
            DoseNumber = t.DoseNumber,
            LadderWeight = t.LadderWeight
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
            t.SetInt("doseNumber", report.DoseNumber);
            t.SetFloat("ladderWeight", report.LadderWeight);
            return t;
        }
    }
}
