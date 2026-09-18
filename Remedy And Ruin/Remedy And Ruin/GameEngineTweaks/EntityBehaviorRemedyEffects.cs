using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Attached to player entities (see Remedy And RuinModSystem.StartServerSide). Owns all
    /// poison/remedy effect state and application.
    ///
    /// OnItemConsumed: called by Patch_RawEating/Patch_MealEating with a consumed item and its
    /// remedyandruinEffect (or remedyandruinEffectByType) attribute, unparsed.
    /// </summary>
    public class EntityBehaviorRemedyEffects : EntityBehavior
    {
        public ITreeAttribute RREffects
        {
            get => entity.WatchedAttributes.GetTreeAttribute("remedyandruinEffects");
            set
            {
                entity.WatchedAttributes.SetAttribute("remedyandruinEffects", value);
                MarkDirty();
            }
        }
        public void MarkDirty() => entity.WatchedAttributes.MarkPathDirty("remedyandruinEffects");
        public void MarkDirty(string key)
        {
            entity.WatchedAttributes.MarkPathDirty("remedyandruinEffects/" + key);
            parseEffectsAndApply();
        }

        private EffectThreadManager threadManager;

        //============== PPEFFECTS ==============//

        public TreeArrayAttribute RRPoisonEffects
        {
            get => RREffects["rrpoisons"] as TreeArrayAttribute;
            set
            {
                if(value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrpoisons"] = arrvalue;
                    MarkDirty("rrpoisons");
                }
            }
        }
        public TreeArrayAttribute RRIllnessEffects
        {
            get => RREffects["rrillness"] as TreeArrayAttribute;
            set
            {
                if (value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrillness"] = arrvalue;
                    MarkDirty("rrillness");
                }
            }
        }
        public TreeArrayAttribute RRPotionEffects
        {
            get => RREffects["rrpotions"] as TreeArrayAttribute;
            set
            {
                if (value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrpotions"] = arrvalue;
                    MarkDirty("rrpotions");
                }
            }
        }

        public bool lastKnownEffectsAdvanceOffline
        {
            get => RREffects.GetBool("lastKnownEffectsAdvanceOffline");
            set
            {
                RREffects.SetBool("lastKnownEffectsAdvanceOffline", value);
                MarkDirty();
            }
        }

        //============== TOLERANCES ==============//

        public int toxicTolerance
        {
            get => RREffects.GetInt("toxicTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("toxicTolerance", value);
                MarkDirty();
            }
        }

        public int noxiousTolerance
        {
            get => RREffects.GetInt("noxiousTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("noxiousTolerance", value);
                MarkDirty();
            }
        }

        public int cardiacTolerance
        {
            get => RREffects.GetInt("cardiacTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("cardiacTolerance", value);
                MarkDirty();
            }
        }

        // Toxic Poison's thread lifetime once its full phase starts (after the onset window):
        // if the tolerance-discounted effect crosses 15% of this exposure's own undiscounted
        // magnitude (02-design-overview.md ~752-761), the DoT itself never ends on its own, so
        // the owning EffectTimerThread's own EndTotalHours has to outlive it too, or the thread's
        // normal end-of-duration poll would tear the "permanent" DoT down almost immediately.
        // 1000 in-game days matches EffectThreadManager's own "effectively forever" reasoning for
        // the DoT's real-world TimeSpan, just expressed in the calendar hours this field is
        // measured in.
        // Below the threshold, the exposure has no DoT at all and genuinely fades on its own -
        // 24h is this plan's own baseline for a poison with no stated fade-out duration.
        private const double ToxicInfiniteThreadLifetimeHours = 1000.0 * 24.0;
        private const double ToxicBelowThresholdFadeHours = 24.0;

        // Mirrors EffectThreadManager's own threshold check (DetermineFullDoTEffect) so this
        // exposure's thread lifetime can be decided at the moment it's created, before dispatch
        // ever runs - both sides read the same toxicTolerance value and apply the same formula,
        // so they can never disagree about whether this exposure crosses.
        private bool ToxicCrossesDoTThreshold(float effectMultiplier)
        {
            float discount = (float)(toxicTolerance / 3) / 9.0f;
            float discounted = Math.Max(0f, effectMultiplier - discount);
            return discounted >= 0.15f * effectMultiplier;
        }

        // Noxious Poison's baseline full-phase duration at effectMultiplier 1.0 and 0 tolerance
        // (02-design-overview.md ~1406) - a flat 24h window, no DoT, ends via Antidote or death.
        private const double NoxiousBaselineDurationHours = 24.0;

        private double NoxiousDurationToleranceMultiplier()
        {
            int tier = noxiousTolerance / 3;
            return Math.Max(0.0, 1.0 - tier / 9.0);
        }

        // Cardiac Poison's baseline full-phase duration at effectMultiplier 1.0 and 0 tolerance
        // (02-design-overview.md ~1418-1429), before the tolerance-discounted duration factor
        // below scales it down.
        private const double CardiacBaselineDurationHours = 6.0;

        // Same tiered 1/9-per-reached-tolerance-tier discount every cluster uses, applied to
        // Cardiac Poison's duration only - the flat HP hit and movement/tool debuffs stay at full
        // strength below full crossing (02-design-overview.md ~845-852); only recovery time
        // shortens as tolerance rises. Reaches exactly 0 at cardiacTolerance == 27, though
        // ApplyEffect's own early-return above already keeps that case from reaching this method.
        private double CardiacDurationToleranceMultiplier()
        {
            int tier = cardiacTolerance / 3;
            return Math.Max(0.0, 1.0 - tier / 9.0);
        }

        public int neurotoxicTolerance
        {
            get => RREffects.GetInt("neurotoxicTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("neurotoxicTolerance", value);
                MarkDirty();
            }
        }

        // Mind Poison's own baseline lifespan at effectMultiplier 1.0 and 0 tolerance
        // (02-design-overview.md ~1445-1456) - like Cardiac Poison's own baseline, this is the
        // exposure's whole lifespan from the moment it's eaten (onset window included), not just
        // the full-phase symptom window measured from onset completion.
        private const double MindPoisonBaselineDurationHours = 24.0;

        private double MindPoisonDurationToleranceMultiplier()
        {
            int tier = brainrotTolerance / 3;
            return Math.Max(0.0, 1.0 - tier / 9.0);
        }

        // Baseline full-phase durations for Neurotoxic Poison's 3-stage ladder
        // (02-design-overview.md ~1431-1443): dose 2 is double dose 1's baseline, dose 3 is quadruple
        // Cardiac Poison's own normal 6h baseline. An arrow-hit's half-weight instance always
        // uses half of dose 1's baseline (3h) regardless of the cumulative dose number it
        // nominally completes - see ApplyNeurotoxicLadder.
        private const double NeurotoxicWeaknessBaselineDurationHours = 6.0;
        private const double NeurotoxicParalysisBaselineDurationHours = 12.0;
        private const double NeurotoxicCardiacArrestBaselineDurationHours = 24.0;
        private const double NeurotoxicArrowHalfDoseBaselineDurationHours = 3.0;

        // Same tiered 1/9-per-reached-tolerance-tier discount every cluster's own effect
        // strength uses (see EffectThreadManager's own Toxic/NoxiousToleranceDiscountedEffect),
        // applied here so Neurotoxic's duration computation doesn't need to reach into
        // EffectThreadManager just to read this entity's own tolerance value.
        private float NeurotoxicToleranceDiscountedEffect(float effectMultiplier)
        {
            float discount = (float)(neurotoxicTolerance / 3) / 9.0f;
            return Math.Max(0f, effectMultiplier - discount);
        }

        /// <summary>
        /// Resolves a Neurotoxic exposure's dose number on the 3-stage ladder and its own
        /// baseline duration, writing both into neweffect. Dose number counts
        /// currently-overlapping Neurotoxic instances only (existingPoisons, the same filtered
        /// list toleranceEligible already built in the caller) - a fully-resolved prior exposure
        /// never contributes, matching the confirmed "currently-overlapping, not lifetime" ladder
        /// semantics.
        ///
        /// Each instance carries its own ladderWeight (1.0 for a drunk dose, 0.5 for an
        /// arrow-hit's half-strength contribution - see Patch_ArrowPoisonDelivery). Dose number
        /// is the ceiling of the summed weight of every currently-active instance including this
        /// new one, clamped to the ladder's 3 real stages. An arrow-hit's own instance always
        /// gets Weakness's bare package at half duration regardless of the dose number it lands
        /// on - only a drunk dose (ladderWeight 1.0) can trigger Paralysis's second-instance
        /// stacking or Cardiac Arrest's package, matching "drinking always advances by exactly
        /// one full stage per dose."
        /// </summary>
        private void ApplyNeurotoxicLadder(TreeAttribute neweffect, EffectStruct effect, List<TreeAttribute> existingPoisons)
        {
            float existingWeight = existingPoisons
                .Where(existing => existing.GetString("cluster") == effect.cluster.ToString())
                .Sum(existing => existing.GetFloat("ladderWeight", 1f));
            float cumulativeWeight = existingWeight + effect.ladderWeight;
            int doseNumber = Math.Min(3, (int)Math.Ceiling(cumulativeWeight));

            double baselineHours;
            if (effect.ladderWeight < 1f)
            {
                baselineHours = NeurotoxicArrowHalfDoseBaselineDurationHours;
            }
            else
            {
                baselineHours = doseNumber switch
                {
                    1 => NeurotoxicWeaknessBaselineDurationHours,
                    2 => NeurotoxicParalysisBaselineDurationHours,
                    _ => NeurotoxicCardiacArrestBaselineDurationHours
                };
            }

            neweffect.SetInt("doseNumber", doseNumber);
            neweffect.SetFloat("ladderWeight", effect.ladderWeight);
            neweffect.SetDouble("timeleft", baselineHours * NeurotoxicToleranceDiscountedEffect(effect.effectMultiplier));
        }

        // Server -> client severity bridge shared by every poison cluster that needs a client-
        // rendered visual driven off a value only this behavior (server-side) knows: keyed by the
        // owning EffectTimerThread's own Guid, one entry per currently-active instance of that
        // cluster's severity, so several overlapping instances (e.g. Neurotoxic's dose 2/3 stacked
        // on top of dose 1) combine correctly instead of one instance's own repeating write
        // overwriting another's out of tick order. The strongest currently-active contribution is
        // what reaches the synced attribute - the same Max-combination
        // TemporalVignetteRenderer's own TempFogStrength/MindPoisonFogStrength already use for
        // their shared visual.
        //
        // The renderer reads each attribute key directly off the local player's WatchedAttributes
        // every frame (mirroring vanilla's own DrunkPerceptionEffect, which reads its
        // "intoxication" WatchedAttributes float the same way - confirmed against VSDecompile)
        // rather than through any dedicated networked message.
        public const string NeurotoxicDrunkWobbleAttributeKey = "remedyandruinNeurotoxicWobble";
        public const string MindPoisonSeverityAttributeKey = "remedyandruinMindPoisonSeverity";

        private readonly ConcurrentDictionary<Guid, float> drunkWobbleContributions = new ConcurrentDictionary<Guid, float>();
        private readonly ConcurrentDictionary<Guid, float> mindPoisonSeverityContributions = new ConcurrentDictionary<Guid, float>();

        // Vanilla's own "psychedelic" attribute (FoodNutritionProperties.Psychedelic, read by
        // PsychedelicPerceptionEffect) - shared, keyed by whichever poison instance is currently
        // holding it, the same multi-contributor pattern as severity/wobble above. Curing the last
        // active contributor writes 0 immediately rather than leaving the value to drain via
        // vanilla's own detox tick.
        private readonly ConcurrentDictionary<Guid, float> psychedelicContributions = new ConcurrentDictionary<Guid, float>();

        public long StartDrunkWobbleContribution(Guid effectGuid, float intensity) =>
            StartSeverityContribution(drunkWobbleContributions, NeurotoxicDrunkWobbleAttributeKey, effectGuid, intensity);

        public void StopDrunkWobbleContribution(Guid effectGuid) =>
            StopSeverityContribution(drunkWobbleContributions, NeurotoxicDrunkWobbleAttributeKey, effectGuid);

        // Mind Poison's one severity value driving both Temporal Fog's screen effect and the
        // drunken camera sway together (02-design-overview.md ~1445-1456) - the renderer reads
        // this single attribute into both.
        public long StartMindPoisonSeverityContribution(Guid effectGuid, float intensity) =>
            StartSeverityContribution(mindPoisonSeverityContributions, MindPoisonSeverityAttributeKey, effectGuid, intensity);

        public void StopMindPoisonSeverityContribution(Guid effectGuid) =>
            StopSeverityContribution(mindPoisonSeverityContributions, MindPoisonSeverityAttributeKey, effectGuid);

        public long StartPsychedelicHold(Guid effectGuid, float intensity) =>
            StartSeverityContribution(psychedelicContributions, "psychedelic", effectGuid, intensity, maxClamp: 2f);

        public void StopPsychedelicHold(Guid effectGuid) =>
            StopSeverityContribution(psychedelicContributions, "psychedelic", effectGuid);

        // Fever's target is a live vanilla behavior field (EntityBehaviorBodyTemperature.
        // CurBodyTemperature), not a WatchedAttributes key, so it can't reuse
        // WriteStrongestContribution directly. Multiple overlapping Noxious instances combine via
        // the same strongest-wins rule as severity/wobble.
        private readonly ConcurrentDictionary<Guid, float> feverContributions = new ConcurrentDictionary<Guid, float>();

        public long StartFeverHold(Guid effectGuid, float temperatureDelta)
        {
            var bodyTemp = entity.GetBehavior<EntityBehaviorBodyTemperature>();
            if (bodyTemp == null) return 0L;

            return entity.World.RegisterGameTickListener(dt =>
            {
                feverContributions[effectGuid] = temperatureDelta;
                WriteStrongestFeverDelta(bodyTemp);
            }, 1000);
        }

        public void StopFeverHold(Guid effectGuid)
        {
            feverContributions.TryRemove(effectGuid, out _);
            var bodyTemp = entity.GetBehavior<EntityBehaviorBodyTemperature>();
            if (bodyTemp != null) WriteStrongestFeverDelta(bodyTemp);
        }

        private void WriteStrongestFeverDelta(EntityBehaviorBodyTemperature bodyTemp)
        {
            float strongest = 0f;
            foreach (float value in feverContributions.Values)
            {
                if (value > strongest) strongest = value;
            }
            bodyTemp.CurBodyTemperature = bodyTemp.NormalBodyTemperature + strongest;
        }

        private long StartSeverityContribution(ConcurrentDictionary<Guid, float> contributions, string attributeKey, Guid effectGuid, float intensity, float maxClamp = 1f)
        {
            float clamped = GameMath.Clamp(intensity, 0f, maxClamp);
            return entity.World.RegisterGameTickListener(dt =>
            {
                contributions[effectGuid] = clamped;
                WriteStrongestContribution(contributions, attributeKey);
            }, 1000);
        }

        private void StopSeverityContribution(ConcurrentDictionary<Guid, float> contributions, string attributeKey, Guid effectGuid)
        {
            contributions.TryRemove(effectGuid, out _);
            WriteStrongestContribution(contributions, attributeKey);
        }

        private void WriteStrongestContribution(ConcurrentDictionary<Guid, float> contributions, string attributeKey)
        {
            float strongest = 0f;
            foreach (float value in contributions.Values)
            {
                if (value > strongest) strongest = value;
            }
            entity.WatchedAttributes.SetFloat(attributeKey, strongest);
        }

        public int brainrotTolerance
        {
            get => RREffects.GetInt("brainrotTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("brainrotTolerance", value);
                MarkDirty();
            }
        }

        private int GetTolerance(string cluster)
        {
            switch (cluster)
            {
                case "TOXICPOISON": return toxicTolerance;
                case "NOXIOUSPOISON": return noxiousTolerance;
                case "CARDIACPOISON": return cardiacTolerance;
                case "NEUROTOXICPOISON": return neurotoxicTolerance;
                case "MINDPOISON": return brainrotTolerance;
                default: return 0;
            }
        }

        private void SetTolerance(string cluster, int value)
        {
            switch (cluster)
            {
                case "TOXICPOISON": toxicTolerance = value; break;
                case "NOXIOUSPOISON": noxiousTolerance = value; break;
                case "CARDIACPOISON": cardiacTolerance = value; break;
                case "NEUROTOXICPOISON": neurotoxicTolerance = value; break;
                case "MINDPOISON": brainrotTolerance = value; break;
            }
        }

        private double GetLastExposureDay(string cluster) => RREffects.GetDouble(cluster + "LastExposureDay", 0.0);

        private void SetLastExposureDay(string cluster, double value)
        {
            RREffects.SetDouble(cluster + "LastExposureDay", value);
            MarkDirty();
        }

        private float GetToleranceDecayAccumulator(string cluster) => RREffects.GetFloat(cluster + "ToleranceDecayAccumulator", 0f);

        private void SetToleranceDecayAccumulator(string cluster, float value)
        {
            RREffects.SetFloat(cluster + "ToleranceDecayAccumulator", value);
            MarkDirty();
        }

        public bool GetPendingToleranceCredit(string cluster) => RREffects.GetBool(cluster + "PendingToleranceCredit", false);

        public void SetPendingToleranceCredit(string cluster, bool value)
        {
            RREffects.SetBool(cluster + "PendingToleranceCredit", value);
            MarkDirty();
        }

        public void RegisterSurvivedExposure(string cluster)
        {
            int current = GetTolerance(cluster);
            SetTolerance(cluster, Math.Min(27, current + 1));
            SetLastExposureDay(cluster, entity.World.Calendar.TotalDays);
            SetToleranceDecayAccumulator(cluster, 0f);
        }

        private static readonly string[] ToleranceClusters = { "TOXICPOISON", "NOXIOUSPOISON", "CARDIACPOISON", "NEUROTOXICPOISON", "MINDPOISON" };

        private long toleranceDecayListenerId;
        private int lastToleranceDecayCheckDay;

        public void ResetToleranceDecayCheckpoint()
        {
            lastToleranceDecayCheckDay = (int)Math.Floor(entity.World.Calendar.TotalDays);
        }

        private void DecayTolerances(float dt)
        {
            int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
            int daysPassed = today - lastToleranceDecayCheckDay;
            if (daysPassed <= 0) return;
            lastToleranceDecayCheckDay = today;

            double daysPerMonth = entity.World.Calendar.DaysPerMonth;

            foreach (string cluster in ToleranceClusters)
            {
                int tolerance = GetTolerance(cluster);
                if (tolerance <= 0) continue;

                double lastExposureDay = GetLastExposureDay(cluster);
                double daysSinceExposure = entity.World.Calendar.TotalDays - lastExposureDay;
                if (daysSinceExposure < daysPerMonth) continue; // still in the grace period

                float accumulator = GetToleranceDecayAccumulator(cluster) + (27f * 0.05f * daysPassed);
                while (accumulator >= 1f && tolerance > 0)
                {
                    tolerance--;
                    accumulator -= 1f;
                }
                SetTolerance(cluster, tolerance);
                SetToleranceDecayAccumulator(cluster, accumulator);
            }
        }

        //============== TOXICITY ==============//

        public float ToxicityCounter
        {
            get => RREffects.GetFloat("toxicityCounter", 0f);
            set
            {
                value = GameMath.Clamp(value, 0f, float.MaxValue);
                RREffects.SetFloat("toxicityCounter", value);
                MarkDirty();
            }
        }

        public void IncreaseToxicity(string cluster, float amount)
        {
            float previous = ToxicityCounter;
            float updated = previous + amount;
            ToxicityCounter = updated;

            if (previous < Remedy_And_RuinModSystem.Config.toxicityOverdoseThreshold
                && updated >= Remedy_And_RuinModSystem.Config.toxicityOverdoseThreshold)
            {
                TriggerOverdose(cluster);
            }
        }

        private void TriggerOverdose(string cluster)
        {
            /*
             * PLACEHOLDER
             * §6's overdose-effect-per-potion-type table decides what actually happens here, once
             * real potion effects exist to construct an overdose instance from. cluster identifies
             * which potion caused this crossing (the only input this method needs later).
             */
        }

        private long toxicityDecayListenerId;

        private void DecayToxicity(float dt)
        {
            float gameSpeedMultiplier = entity.World.Calendar.SpeedOfTime * entity.World.Calendar.CalendarSpeedMul / 30f;
            float decayAmount = Remedy_And_RuinModSystem.Config.toxicityDecayPerRealSecond * gameSpeedMultiplier;
            if (decayAmount <= 0f) return;

            float current = ToxicityCounter;
            if (current <= 0f) return;

            ToxicityCounter = Math.Max(0f, current - decayAmount);
        }

        //============== PROPERTIES ===============//

        bool     drankAntidote = false;
        DateTime timeAntidoteConsumed = DateTime.MinValue;

        //============== CONSTRUCTORS ==============//

        bool suppressEffect = false;

        public EntityBehaviorRemedyEffects(Entity entity) : base(entity)
        {
            //if (entity.World.Side != EnumAppSide.Server) return;
            if (!entity.WatchedAttributes.HasAttribute("remedyandruinEffects"))
            {
                entity.WatchedAttributes.SetAttribute("remedyandruinEffects", new TreeAttribute());
            }
            if (RREffects["rrpoisons"] == null)
            {
                suppressEffect = true;
                RREffects["rrpoisons"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrpoisons");
            }
            if (RREffects["rrillness"] == null)
            {
                suppressEffect = true;
                RREffects["rrillness"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrillness");
            }
            if (RREffects["rrpotions"] == null)
            {
                suppressEffect = true;
                RREffects["rrpotions"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrpotions");
            }
            suppressEffect = false;
            threadManager = new EffectThreadManager(entity);
            toxicityDecayListenerId = entity.World.RegisterGameTickListener(DecayToxicity, 1000);
            lastToleranceDecayCheckDay = (int)Math.Floor(entity.World.Calendar.TotalDays);
            toleranceDecayListenerId = entity.World.RegisterGameTickListener(DecayTolerances, 60000);
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            if (toxicityDecayListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(toxicityDecayListenerId);
            }
            if (toleranceDecayListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(toleranceDecayListenerId);
            }
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);
            threadManager.HandleForcefulEnd();
        }

        public void DestroyProgress()
        {
            //I warned you not to.
            brainrotTolerance = 0;
            cardiacTolerance = 0;
            neurotoxicTolerance = 0;
            noxiousTolerance = 0;
            toxicTolerance = 0;
            RREffects["rrpoisons"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrpoisons");
            RREffects["rrillness"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrillness");
            RREffects["rrpotions"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrpotions");
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        public bool HandleDisconnect() => threadManager.HandleDisconnect();

        public bool HandleGameWorldSaving() => threadManager.HandleGameWorldSaving();

        public void ReconstructActiveEffectsOnLogin() => parseEffectsAndApply();

        public override string PropertyName()
        {
            return "remedyandruinEffects";
        }

        //============== Data Structures ==============//

        public enum EffectCluster
        {
            ANALGESIC,
            ANTIDOTE,
            ANTINAUSEA,
            ANTISEPTIC,
            ANTIVIRAL,
            MINDTONIC,
            SEDATIVE,
            TONIC,
            TOXICPOISON,
            NOXIOUSPOISON,
            CARDIACPOISON,
            NEUROTOXICPOISON,
            MINDPOISON,
            TOPICALOINTMENT
        }

        public enum EffectType
        {
            POISON,
            ILLNESS
        }

        public struct EffectStruct (EffectCluster inval)
        {
            public EffectCluster cluster = inval;
            public bool   isConcentrated        = false; //base/potion or concentrate?
            public bool   isPoison              = false; //poison or potion?
            public double timestarted          = 0.0;    //used by all effects with duration
            public double timeleft             = 0.0;    //used by all effects with duration
            public float  effectMultiplier      = 0.0f;  //used by all poisons
            public float  onsetMultiplier       = 0.0f;  //used by some poisons
            public float  toxicEffectMultiplier = 0.0f;  //used by dual-effect mushrooms
            public float  toxicOnsetMultiplier  = 0.0f;  //used by dual-effect mushrooms
            public float  ladderWeight          = 1.0f;  //Neurotoxic-only: how much this exposure counts toward its dose number - 1.0 for a drunk dose, 0.5 for an arrow hit
        }

        //============== EVENT HANDLERS ==============//

        public void OnItemConsumed(ItemStack consumedStack, JsonObject effectData, float potencyScale = 1.0f)
        {
            // Convert effectData.cluster to uppercase for consistency and fill in defaults/parse data
            EffectStruct effect = new EffectStruct(effectData["cluster"].AsString().ToUpper().ToEnum<EffectCluster>());

            if (effectData.KeyExists("tier")) { effect.isConcentrated = effectData["isConcentrated"].AsBool(); }
            if (effectData.KeyExists("isPoison")) { effect.isPoison = effectData["isPoison"].AsBool(); }
            if (effectData.KeyExists("effectMultiplier"))
            {
                effect.effectMultiplier = effectData["effectMultiplier"].AsFloat() * potencyScale;
            }
            if (effectData.KeyExists("onsetMultiplier"))
            {
                effect.onsetMultiplier = effectData["onsetMultiplier"].AsFloat();
            }
            if (effectData.KeyExists("toxicEffectMultiplier"))
            {
                effect.toxicEffectMultiplier = effectData["toxicEffectMultiplier"].AsFloat() * potencyScale;
            }
            if (effectData.KeyExists("toxicOnsetMultiplier"))
            {
                effect.toxicOnsetMultiplier = effectData["toxicOnsetMultiplier"].AsFloat();
            }

            ApplyEffect(effect);

        }

        // Tracks which effect guids already have a running EffectTimerThread, so parseEffectsAndApply
        // doesn't spin up a duplicate thread for an effect it's already seen.
        private Dictionary<string, bool> effectsApplied = new Dictionary<string, bool>();

        void parseEffectsAndApply()
        {
            if (suppressEffect) return;
            var liveKeys = new HashSet<string>();
            TreeAttribute[] rrpoisons = RRPoisonEffects.value;
            foreach (var poison in rrpoisons)
            {
                string effectname = poison.GetString("effectname");
                string guid = effectname.Split("|")[1];
                liveKeys.Add(guid);
                if (!effectsApplied.ContainsKey(guid))
                {
                    effectsApplied[guid] = true;
                    threadManager.ApplyPoisonEffect(guid);
                }
            }
            TreeAttribute[] rrillnesses = RRIllnessEffects.value;
            foreach (var illness in rrillnesses)
            {
                string effectname = illness.GetString("effectname");
                string guid = effectname.Split("|")[1];
                liveKeys.Add(guid);
                if (!effectsApplied.ContainsKey(guid))
                {
                    effectsApplied[guid] = true;
                    threadManager.ApplyIllnessEffect(guid);
                }
            }

            TreeAttribute[] rrpotions = RRPotionEffects.value;
            foreach (var potion in rrpotions)
            {
                string effectname = potion.GetString("effectname");
                string guid = effectname.Split("|")[1];
                liveKeys.Add(guid);
                if (!effectsApplied.ContainsKey(guid))
                {
                    effectsApplied[guid] = true;
                    threadManager.ApplyPotionEffect(guid);
                }
            }

            foreach (var staleKey in effectsApplied.Keys.Except(liveKeys).ToList())
            {
                effectsApplied.Remove(staleKey);
            }
        }

        public void ApplyEffect(EffectStruct effect) => ApplyEffect(effect, forceIneligibleForTolerance: false);

        // only called when an item is eaten or an arrow lands so it can never apply an illness.
        public void ApplyEffect(EffectStruct effect, bool forceIneligibleForTolerance)
        {
            Guid uid = Guid.NewGuid();
            string effectname = effect.cluster.ToString() + "|" + uid.ToString();
            bool poison = false;
            bool antidote = false;
            switch (effect.cluster)
            {
                case EffectCluster.ANALGESIC:
                    break;
                case EffectCluster.ANTIDOTE:
                    /*
                     * ANTIDOTE:
                     *     - used to indicate antidote
                     *     - drinking once induces vomiting and voids satiety
                     *     - drinking again within one IRL minute removes all poison effects, completely, and entirely.
                     *     - poison effects lost this way do not count towards building tolerance
                     */
                antidote = true;
                    break;
                case EffectCluster.ANTINAUSEA:
                    break;
                case EffectCluster.ANTISEPTIC:
                    break;
                case EffectCluster.ANTIVIRAL:
                    break;
                case EffectCluster.MINDTONIC:
                    break;
                case EffectCluster.SEDATIVE:
                    break;
                case EffectCluster.TONIC:
                    break;
                case EffectCluster.TOXICPOISON:
                    /*
                     * TOXIC POISON :
                     *     - used to indicate liverbane aka liver failure
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(toxicTolerance / 3) / 9.0f
                     *     - apply healingeffectiveness reduction for a certain amount of time
                     *     - once timer expires, add DoT effect if effect > 0.15; DoT never expires
                     *     - if effect < 0.15, do not apply DoT and remove healingeffectiveness reduction
                     *     - surviving this awards 1/27 of progression towards toxicTolerance.
                     */
                    poison = true;
                    effect.timeleft = ToxicCrossesDoTThreshold(effect.effectMultiplier)
                        ? ToxicInfiniteThreadLifetimeHours
                        : ToxicBelowThresholdFadeHours;
                    break;
                case EffectCluster.NOXIOUSPOISON:
                    /*
                     * NOXIOUS POISON :
                     *     - used to indicate gutbane aka GI irritation
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(noxiousTolerance / 3) / 9.0f
                     *     - apply psychedelic trip effect for a certain amount of time (same as eating a psychedelic mushroom)
                     *     - trigger Vomiting loop for a certain amount of time, lessened by effect multiplier
                     *     - surviving this awards 1/27 of progression towards noxiousTolerance
                     */
                    poison = true;
                    effect.timeleft = NoxiousBaselineDurationHours * effect.effectMultiplier * NoxiousDurationToleranceMultiplier();
                    break;
                case EffectCluster.CARDIACPOISON:
                    /*
                     * CARDIAC POISON :
                     *     - used to indicate heartbane aka heart failure
                     *     - flat -5 current/max HP hit at full strength regardless of tolerance
                     *       below full (9/9) crossing - only duration scales down with tolerance
                     *     - movement-speed and tool-use debuffs, 6h baseline duration scaled by
                     *       effectMultiplier and the same tolerance-discounted duration factor
                     *     - exertion (sprinting or tool use) during the active episode adds
                     *       uncapped stacks: +1h duration and another -5 HP each
                     *     - at full (9/9) tolerance the entire exposure is voided outright - see
                     *       the early return below, mirroring the post-Antidote immunity window's
                     *       own "this exposure never happened" no-op further down this method
                     *     - surviving this awards 1/27 of progression towards cardiacTolerance
                     */
                    if (cardiacTolerance >= 27)
                    {
                        return;
                    }
                    poison = true;
                    effect.timeleft = CardiacBaselineDurationHours * effect.effectMultiplier * CardiacDurationToleranceMultiplier();
                    break;
                case EffectCluster.NEUROTOXICPOISON:
                    /*
                     * NEUROTOXIC POISON :
                     *     - used to indicate nervebane aka nerve damage
                     *     - dose number = how many Neurotoxic instances are currently
                     *       overlapping (including this one), weighted by ladderWeight - see
                     *       ApplyNeurotoxicLadder, called below once the currently-active list
                     *       is available
                     *     - dose 1: Weakness (-40% walkspeed), 6h baseline
                     *     - dose 2: a second, independent Weakness instance (Paralysis), 12h
                     *       baseline - the two overlapping walkspeed modifiers sum via
                     *       entity.Stats' own additive blending
                     *     - dose 3: Cardiac Poison's own full package (flat HP hit, movement/tool
                     *       debuffs, exertion-stacking), 24h baseline
                     *     - headache (rangedWeaponsAcc penalty) and dizziness (DrunkWobbleStrength)
                     *       apply at every stage
                     *     - surviving this awards 1/27 of progression towards neurotoxicTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.MINDPOISON:
                    /*
                     * MIND POISON :
                     *     - used to indicate Brain Rot aka mind damage
                     *     - one tolerance-discounted severity value (see
                     *       EffectThreadManager.MindPoisonToleranceDiscountedEffect) drives
                     *       Temporal Fog's screen effect, the drunken camera sway, and
                     *       Hallucination's apparition-spawn scaling together
                     *     - genuine psychedelic tripping (vanilla's own psychedelic-attribute
                     *       mushroom effect, reused via StartPsychedelicHold)
                     *     - each genuine movement attempt during the effect rolls a chance of
                     *       vomiting (see EffectThreadManager's move-vomit watcher)
                     *     - double hunger rate
                     *     - no DoT, no max-health modifier - deliberately non-lethal
                     *     - 24h baseline lifespan, scaled by effectMultiplier and discounted by
                     *       tolerance the same way Cardiac Poison's own duration is
                     *     - surviving this awards 1/27 of progression towards brainrotTolerance
                     */
                    poison = true;
                    effect.timeleft = MindPoisonBaselineDurationHours * effect.effectMultiplier * MindPoisonDurationToleranceMultiplier();
                    break;
                case EffectCluster.TOPICALOINTMENT:
                    break;
            }
            //write to treeArrayAttribute
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectname", effectname);
            neweffect.SetString("cluster", effect.cluster.ToString());
            neweffect.SetBool("isConcentrated", effect.isConcentrated);
            neweffect.SetDouble("timestarted", effect.timestarted);
            neweffect.SetDouble("timeleft", effect.timeleft);
            neweffect.SetFloat("effectMultiplier", effect.effectMultiplier);
            neweffect.SetFloat("toxicEffectMultiplier", effect.toxicEffectMultiplier);
            neweffect.SetFloat("toxicOnsetMultiplier", effect.toxicOnsetMultiplier);
            if (poison)
            {
                if (!IsPostAntidoteWindowActive())
                {
                    List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
                    bool toleranceEligible = !forceIneligibleForTolerance
                        && !rrpoisons.Any(existing => existing.GetString("cluster") == effect.cluster.ToString());
                    neweffect.SetBool("toleranceEligible", toleranceEligible);
                    neweffect.SetBool("isPoison", true);
                    neweffect.SetFloat("onsetMultiplier", effect.onsetMultiplier); //only used for poisons

                    if (effect.cluster == EffectCluster.NEUROTOXICPOISON)
                    {
                        ApplyNeurotoxicLadder(neweffect, effect, rrpoisons);
                    }

                    rrpoisons.Add(neweffect);
                    RRPoisonEffects = new TreeArrayAttribute(rrpoisons.ToArray());
                }
                // else: poison immunity is active during the post-Antidote window - this exposure
                // never happened at all.
            }
            else if (!antidote)
            {
                if (IsPostAntidoteWindowActive() && new Random().NextDouble() < 0.5)
                {
                    // 50% chance: the potion is voided entirely and vomiting triggers, per the
                    // restricted-diet window's potion-risk rule - the potion is never added to
                    // RRPotionEffects at all, so its effect never applies.
                    TriggerAntidoteWindowVomit();
                }
                else
                {
                    List<TreeAttribute> rrpotions = RRPotionEffects.value.ToList<TreeAttribute>();
                    neweffect.SetBool("isPoison", false);
                    rrpotions.Add(neweffect);
                    RRPotionEffects = new TreeArrayAttribute(rrpotions.ToArray());
                }
            }
            if (antidote)
            {
                if (!drankAntidote || timeAntidoteConsumed.AddSeconds(60) <= DateTime.Now)
                {
                    // First dose of a fresh sequence - either genuinely the first dose, or a stale
                    // sequence whose 60-second window already lapsed without a valid second dose
                    // landing. Either way this dose starts over; it never continues a dead sequence.
                    timeAntidoteConsumed = DateTime.Now;
                    drankAntidote = true;
                    VoidStomachContents(new Random().NextDouble());
                }
                else
                {
                    // Second dose, landing within the window - the cure actually takes effect.
                    // HandleForcefulEnd must run before RRPoisonEffects is cleared: it stops every
                    // tracked poison/illness thread's severity contributions, tick listeners, stat
                    // modifiers, DoT, and max-health modifiers via the same
                    // RemoveGuidsFromWatchedAttributes path every other effect end uses, which only
                    // removes the WatchedAttributes entries it actually confirmed stopped. Clearing
                    // RRPoisonEffects first would delete the data for every poison unconditionally
                    // while leaving anything HandleForcefulEnd doesn't know about - stale
                    // contribution state, an orphaned tick listener still writing its value back
                    // every second - running forever with nothing left to ever stop it. Any entry
                    // still present after HandleForcefulEnd genuinely had no matching thread;
                    // sweeping it here is a safety net, not the primary removal path.
                    threadManager.HandleForcefulEnd();
                    if (RRPoisonEffects.value.Length > 0)
                    {
                        RRPoisonEffects = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                    }
                    // The arrow-delivered Toxic bonus DoT isn't a tracked EffectTimerThread (it's
                    // injected directly by Patch_ArrowPoisonDelivery), so HandleForcefulEnd above
                    // never sees it - stop it separately by its own fixed effect-type id. A no-op
                    // if none is currently active.
                    entity.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect(EffectThreadManager.ArrowBonusToxicDoTEffectType);
                    drankAntidote = false;
                    timeAntidoteConsumed = DateTime.MinValue;
                    ApplyAntidoteAftermathEffect();
                }
            }

            // A mushroom carrying a secondary Toxic sliver (toxicEffectMultiplier > 0 - Noxious
            // Poison's non-Witch's-Hat entries, Mind Poison's Laughing Jim/Fly Agaric) spawns a
            // fully independent Toxic Poison exposure alongside its primary effect: its own timer,
            // its own tolerance track, going through this exact same ApplyEffect path a primary
            // Toxic exposure would use - not a modifier layered onto the primary instance.
            if (poison && effect.cluster != EffectCluster.TOXICPOISON && effect.toxicEffectMultiplier > 0f)
            {
                EffectStruct secondary = new EffectStruct(EffectCluster.TOXICPOISON)
                {
                    isPoison = true,
                    effectMultiplier = effect.toxicEffectMultiplier,
                    onsetMultiplier = effect.toxicOnsetMultiplier
                };
                ApplyEffect(secondary, forceIneligibleForTolerance);
            }
        }

        public void OnAnyItemConsumed(ItemStack stack, IWorldAccessor world)
        {
            if (drankAntidote && !IsAntidoteItem(stack))
            {
                // Consuming literally anything else between the Antidote's two doses resets the
                // sequence - only the Antidote's own two doses ever advance it.
                drankAntidote = false;
                timeAntidoteConsumed = DateTime.MinValue;
            }

            if (IsPostAntidoteWindowActive() && !AntidoteWindowFoodRules.IsSafeRawItem(stack, world))
            {
                TriggerAntidoteWindowVomit();
            }
        }

        public void TriggerAntidoteWindowVomit()
        {
            TriggerVomit();
        }

        // A guaranteed vomit event - the same VoidStomachContents(1.0) call the Antidote window,
        // the repeating vomit-roll, and Mind Poison's move-triggered vomiting all use, exposed
        // publicly since callers outside this class (EffectThreadManager's per-cluster tick
        // listeners) have no access to the private VoidStomachContents itself.
        public void TriggerVomit()
        {
            VoidStomachContents(1.0);
        }

        private static bool IsAntidoteItem(ItemStack stack)
        {
            string cluster = stack?.Collectible?.Attributes?["remedyandruinEffect"]?["cluster"]?.AsString();
            return string.Equals(cluster, "ANTIDOTE", StringComparison.OrdinalIgnoreCase);
        }

        public void OnMealConsumed(IWorldAccessor world, ItemStack containerStack, ItemStack[] contentStacks, BlockMeal block)
        {
            if (drankAntidote)
            {
                drankAntidote = false;
                timeAntidoteConsumed = DateTime.MinValue;
            }

            if (IsPostAntidoteWindowActive() && !AntidoteWindowFoodRules.IsSafeMeal(world, containerStack, contentStacks, block))
            {
                TriggerAntidoteWindowVomit();
            }
        }

        private void ApplyAntidoteAftermathEffect()
        {
            Guid uid = Guid.NewGuid();
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectname", "ANTIDOTEAFTERMATH|" + uid.ToString());
            neweffect.SetString("cluster", "ANTIDOTEAFTERMATH");
            neweffect.SetBool("isConcentrated", false);
            neweffect.SetBool("isPoison", false);
            neweffect.SetDouble("timestarted", 0.0);
            neweffect.SetDouble("timeleft", 2.0); // 2 in-game hours - the single shared window duration
            neweffect.SetFloat("effectMultiplier", 1.0f);
            neweffect.SetFloat("toxicEffectMultiplier", 0f);
            neweffect.SetFloat("toxicOnsetMultiplier", 0f);

            List<TreeAttribute> rrpotions = RRPotionEffects.value.ToList<TreeAttribute>();
            rrpotions.Add(neweffect);
            RRPotionEffects = new TreeArrayAttribute(rrpotions.ToArray());
        }

        public bool IsPostAntidoteWindowActive()
        {
            return RRPotionEffects.value.Any(e => e.GetString("cluster") == "ANTIDOTEAFTERMATH");
        }

        private void VoidStomachContents(double chance)
        {
            float amountToDrain = 0.0f;
            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if(hunger != null)
            {
                amountToDrain = hunger.Saturation;
                var stomach = entity.GetBehavior("expandedstomach");
                if (stomach != null)
                {
                    float? currentStomachAmount = stomach.GetType().GetProperty("ExpandedStomachMeter")?.GetValue(stomach) as float?;
                    if (currentStomachAmount.HasValue)
                    {
                        stomach.GetType().GetProperty("ExpandedStomachMeter")?.SetValue(stomach, 0f); //drain stomach first so it doesn't intercept the next call
                    }
                }
                hunger.Saturation -= amountToDrain; //ExpandedStomach will intercept this if it's not empty...
            }
            //TODO: wire in chance for additional void events if not triggered by antidote
        }

        private const double MaxVomitRollIntervalSeconds = 600.0;

        /// <summary>
        /// Registers a repeating, randomized vomit roll: once each interval elapses it always
        /// vomits (VoidStomachContents(1.0), not a probability check - the interval itself is the
        /// randomization). Each cycle's interval is baseIntervalSeconds jittered by +/-1/3 (pass
        /// the midpoint of the desired spread, e.g. 45 for a 30-60s range at full strength),
        /// divided by effectiveMultiplier (floored at minMultiplierFloor so a heavily
        /// tolerance-discounted dose still eventually rolls) and capped at
        /// MaxVomitRollIntervalSeconds. Returns the game tick listener id so the caller can
        /// unregister it once its own effect ends - this method has no opinion on that lifetime.
        /// Not for Mind Poison's move-triggered vomiting, which is event-driven rather than
        /// interval-driven and needs its own mechanism.
        /// </summary>
        public long StartRepeatingVomitRoll(double baseIntervalSeconds, float effectiveMultiplier, float minMultiplierFloor = 0.05f)
        {
            double elapsedSeconds = 0.0;
            double targetSeconds = NextVomitRollIntervalSeconds(baseIntervalSeconds, effectiveMultiplier, minMultiplierFloor);

            return entity.World.RegisterGameTickListener(dt =>
            {
                elapsedSeconds += dt;
                if (elapsedSeconds < targetSeconds) return;

                elapsedSeconds = 0.0;
                targetSeconds = NextVomitRollIntervalSeconds(baseIntervalSeconds, effectiveMultiplier, minMultiplierFloor);
                VoidStomachContents(1.0);
            }, 1000);
        }

        private static double NextVomitRollIntervalSeconds(double baseIntervalSeconds, float effectiveMultiplier, float minMultiplierFloor)
        {
            double jitteredSeconds = baseIntervalSeconds * (2.0 / 3.0 + new Random().NextDouble() * (2.0 / 3.0));
            double scaledSeconds = jitteredSeconds / Math.Max(effectiveMultiplier, minMultiplierFloor);
            return Math.Min(scaledSeconds, MaxVomitRollIntervalSeconds);
        }

    }

    public static class Helpers
    {
        public static T ToEnum<T>(this string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }
    }
}
