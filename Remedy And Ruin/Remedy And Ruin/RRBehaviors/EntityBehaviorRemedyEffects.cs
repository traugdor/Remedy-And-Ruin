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
using Remedy_And_Ruin.GameEngineTweaks;

namespace Remedy_And_Ruin.RRBehaviors
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

        // Every poison's strength is measured in vial charges: 1.0 is one charge of the regular
        // potion, and each tolerance tier (tolerance / 3, 0-9) cancels PoisonStrengthPerToleranceTier
        // of it. Strength scales a cluster's intensity only; durations are fixed per cluster.
        //
        // For Toxic Poison, 1.0 deals ToxicDoTPerSecondPerStrength HP/s. An exposure with any
        // strength left after tolerance runs its DoT for ToxicDoTRealSeconds after onset, then ends
        // naturally, which is what awards tolerance credit. With none left there is no DoT and the
        // healing dip fades after ToxicBelowThresholdFadeHours.
        internal const float ToxicDoTPerSecondPerStrength = 1.5f;
        internal const float PoisonStrengthPerToleranceTier = 1.0f;

        /// <summary>Strength left after the given tolerance counter's tiers have each cancelled one charge; never negative.</summary>
        internal static float StrengthAfterTolerance(float strength, int tolerance) =>
            Math.Max(0f, strength - (tolerance / 3) * PoisonStrengthPerToleranceTier);
        internal const double ToxicDoTRealSeconds = 10.0;
        private const double ToxicBelowThresholdFadeHours = 24.0;

        // Thread lifetime in game hours, measured from the moment the exposure starts, so it
        // spans the onset window plus the DoT's own duration.
        private double ToxicCrossingThreadLifetimeHours(float onsetMultiplier)
        {
            double onsetHours = GameEngineTweaks.EffectThreadManager.ToxicOnsetHours(onsetMultiplier);
            return onsetHours + GameEngineTweaks.CalendarTimeHelper.RealSecondsToGameHours(entity.World.Calendar, ToxicDoTRealSeconds);
        }

        // Mirrors EffectThreadManager's own check (DetermineFullDoTEffect) so this exposure's
        // thread lifetime can be decided at the moment it's created, before dispatch ever runs -
        // both sides read the same toxicTolerance value and apply the same formula, so they can
        // never disagree about whether this exposure has a DoT.
        private bool ToxicHasDoT(float effectMultiplier)
        {
            return StrengthAfterTolerance(effectMultiplier, toxicTolerance) > 0f;
        }

        // Fixed full-phase durations for Noxious, Cardiac and Mind Poison: any exposure with
        // strength left after tolerance lasts this long, regardless of how strong it is.
        private const double NoxiousBaselineDurationHours = 24.0;
        private const double CardiacBaselineDurationHours = 6.0;

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

        // Mind Poison's lifespan is the exposure's whole lifespan from the moment it's eaten
        // (onset window included), not just the full-phase symptom window.
        private const double MindPoisonBaselineDurationHours = 24.0;

        // Neurotoxic Poison's three ladder rungs and their fixed durations. An arrow-hit's
        // half-weight instance (ladderWeight below 1) always uses the arrow duration regardless
        // of its strength - see ApplyNeurotoxicLadder.
        private const double NeurotoxicWeaknessBaselineDurationHours = 6.0;
        private const double NeurotoxicParalysisBaselineDurationHours = 12.0;
        private const double NeurotoxicCardiacArrestBaselineDurationHours = 24.0;
        private const double NeurotoxicArrowHalfDoseBaselineDurationHours = 3.0;

        // Neurotoxic rung thresholds on tolerance-discounted strength: below 2 is Weakness, 2 up
        // to 3 is Paralysis, 3 and above adds Cardiac Poison's package.
        internal const float NeurotoxicParalysisMinStrength = 2f;
        internal const float NeurotoxicCardiacArrestMinStrength = 3f;

        internal static int NeurotoxicRungForStrength(float discountedStrength) =>
            discountedStrength >= NeurotoxicCardiacArrestMinStrength ? 3
            : discountedStrength >= NeurotoxicParalysisMinStrength ? 2
            : 1;

        /// <summary>
        /// Resolves a Neurotoxic exposure's ladder rung and its fixed duration, writing both into
        /// neweffect. The rung comes from the exposure's own tolerance-discounted strength (see
        /// NeurotoxicRungForStrength), so one large dose lands on a high rung directly. An
        /// arrow-hit's half-weight instance (ladderWeight 0.5 - see Patch_ArrowPoisonDelivery)
        /// always gets Weakness's bare package at half duration, whatever its strength.
        /// </summary>
        private void ApplyNeurotoxicLadder(TreeAttribute neweffect, EffectStruct effect)
        {
            int doseNumber = NeurotoxicRungForStrength(StrengthAfterTolerance(effect.effectMultiplier, neurotoxicTolerance));

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
            neweffect.SetDouble("timeleft", baselineHours);
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
        private long analgesicRefreshListenerId;
        private long hallucinationPollListenerId;

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
            if (entity.World.Side == EnumAppSide.Server)
            {
                analgesicRefreshListenerId = entity.World.RegisterGameTickListener(dt => RefreshAnalgesicScales(), 1000);
                hallucinationPollListenerId = entity.World.RegisterGameTickListener(dt =>
                {
                    PollOverdoseHallucination();
                    ClearStaleSedativeAssistedFlag();
                }, 1000);
            }
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        private float appliedPainScale = float.NaN;
        private float appliedWalkPenaltyScale = float.NaN;

        /// <summary>
        /// Re-applies every stat modifier that depends on the Analgesic scales (Wound Infection and
        /// Flu walk penalties, broken-bone leg and arm penalties) when either scale differs from
        /// the one last applied. Runs every second, so a dose starting or its window ending
        /// (including through sleep, relog and death) takes effect within a second. Server only.
        /// </summary>
        private void RefreshAnalgesicScales(bool force = false)
        {
            if (entity.World.Side != EnumAppSide.Server || !(entity is EntityAgent agent)) return;

            float pain = PotionEffects.PainScale(entity);
            float walk = PotionEffects.WalkPenaltyScale(entity);
            if (!force && pain == appliedPainScale && walk == appliedWalkPenaltyScale) return;

            appliedPainScale = pain;
            appliedWalkPenaltyScale = walk;

            entity.GetBehavior<EntityBehaviorPlayerConditions>()?.RefreshWoundInfectionWalk(agent);
            entity.GetBehavior<EntityBehaviorChestColdFamily>()?.RefreshWalkPenalty(agent);
            EntityBehaviorBrokenBones bones = entity.GetBehavior<EntityBehaviorBrokenBones>();
            bones?.ApplyLegEffects();
            bones?.ApplyArmEffects();
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
            if (analgesicRefreshListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(analgesicRefreshListenerId);
            }
            if (hallucinationPollListenerId != 0)
            {
                entity.World.UnregisterGameTickListener(hallucinationPollListenerId);
            }
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);
            threadManager.HandleForcefulEnd(includePotions: true);
            if (RRPotionEffects.value.Length > 0)
            {
                RRPotionEffects = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            }
            entity.WatchedAttributes.SetDouble(DiscoloredMeatToxinsKey, 0.0);
            entity.WatchedAttributes.MarkPathDirty(DiscoloredMeatToxinsKey);
            ClearOverdoseHallucination();
            entity.WatchedAttributes.RemoveAttribute(PotionEffects.TopicalSatietyCutEndKey);
        }

        /// <summary>
        /// Removes every active remedy dose record and the state derived from them: the overdose
        /// Hallucination (pending and running), the Topical Ointment satiety cut and the
        /// sedative-assisted sleep flag. Poisons and illnesses are untouched. Server only.
        /// </summary>
        public void ClearRemedyDoses()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            threadManager.HandleForcefulEnd(potionsOnly: true);
            if (RRPotionEffects.value.Length > 0)
            {
                RRPotionEffects = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            }
            ClearOverdoseHallucination();
            entity.WatchedAttributes.RemoveAttribute(PotionEffects.TopicalSatietyCutEndKey);
            entity.WatchedAttributes.RemoveAttribute(SleepCompletionEvents.SedativeAssistedSleepAttributeKey);
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
            entity.GetBehavior<EntityBehaviorUpsetStomach>()?.ClearAll();
            ClearOverdoseHallucination();
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
            public bool   isPoison             = false; //poison or potion?
            public double timestarted          = 0.0;    //used by all effects with duration
            public double timeleft             = 0.0;    //used by all effects with duration
            public float  effectMultiplier      = 0.0f;  //used by all poisons
            public float  onsetMultiplier       = 0.0f;  //used by some poisons
            public float  toxicEffectMultiplier = 0.0f;  //used by dual-effect mushrooms
            public float  toxicOnsetMultiplier  = 0.0f;  //used by dual-effect mushrooms
            public float  ladderWeight          = 1.0f;  //Neurotoxic-only: how much this exposure counts toward its dose number - 1.0 for a drunk dose, 0.5 for an arrow hit
            public int    doseCount             = 1;     //remedies only: how many 0.25 L doses this drink contained (a 1 L gulp is 4)
        }

        //============== EVENT HANDLERS ==============//

        public void OnItemConsumed(ItemStack consumedStack, JsonObject effectData, float potencyScale = 1.0f, int doseCount = 1, bool fromVial = false)
        {
            // Convert effectData.cluster to uppercase for consistency and fill in defaults/parse data
            EffectStruct effect = new EffectStruct(effectData["cluster"].AsString().ToUpper().ToEnum<EffectCluster>());

            effect.doseCount = Math.Max(1, doseCount);

            if (effectData.KeyExists("isPoison")) { effect.isPoison = effectData["isPoison"].AsBool(); }
            if (effectData.KeyExists("effectMultiplier"))
            {
                // Vial charge scaling applies to poisons only; a remedy's effectMultiplier is its tier.
                // Poison strength is counted in charges: a vial contributes its charge count (9 times
                // potencyScale), anything else (a mushroom, a bucket) contributes its listed value as-is.
                float scale = PotionEffects.IsRemedyCluster(effect.cluster) ? 1.0f
                    : (fromVial ? potencyScale * 9f : 1.0f);
                effect.effectMultiplier = effectData["effectMultiplier"].AsFloat() * scale;
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
            // A Diluted remedy (effectMultiplier 0) is inert: no record and no side effects.
            if (PotionEffects.IsRemedyCluster(effect.cluster) && effect.effectMultiplier <= 0f)
            {
                return;
            }

            Guid uid = Guid.NewGuid();
            string effectname = effect.cluster.ToString() + "|" + uid.ToString();
            bool poison = false;
            bool antidote = false;
            // A poison fully cancelled by tolerance records no exposure; a secondary Toxic sliver
            // is still applied against its own tolerance below.
            bool negated = false;
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
                     *     - once onset completes, add a 10 real-second DoT if any effect is left after tolerance; its natural end awards tolerance
                     *     - if effect < 0.15, do not apply DoT and remove healingeffectiveness reduction
                     *     - surviving this awards 1/27 of progression towards toxicTolerance.
                     */
                    poison = true;
                    effect.timeleft = ToxicHasDoT(effect.effectMultiplier)
                        ? ToxicCrossingThreadLifetimeHours(effect.onsetMultiplier)
                        : ToxicBelowThresholdFadeHours;
                    break;
                case EffectCluster.NOXIOUSPOISON:
                    /*
                     * NOXIOUS POISON :
                     *     - used to indicate gutbane aka GI irritation
                     *     - strength left after tolerance (see StrengthAfterTolerance) scales fever, tripping and vomit frequency
                     *     - no strength left: the exposure never happens
                     *     - psychedelic trip effect (same as eating a psychedelic mushroom) and a repeating vomiting loop for the fixed 24h duration
                     *     - surviving this awards 1/27 of progression towards noxiousTolerance
                     */
                    poison = true;
                    negated = StrengthAfterTolerance(effect.effectMultiplier, noxiousTolerance) <= 0f;
                    effect.timeleft = NoxiousBaselineDurationHours;
                    break;
                case EffectCluster.CARDIACPOISON:
                    /*
                     * CARDIAC POISON :
                     *     - used to indicate heartbane aka heart failure
                     *     - -5 max HP per point of strength left after tolerance, plus
                     *       movement-speed and tool-use debuffs that scale with it, for the fixed
                     *       6h duration
                     *     - exertion (sprinting or tool use) during the active episode adds
                     *       uncapped stacks: +1h duration and another -5 HP each, scaled by strength
                     *     - no strength left: the exposure never happens
                     *     - surviving this awards 1/27 of progression towards cardiacTolerance
                     */
                    poison = true;
                    negated = StrengthAfterTolerance(effect.effectMultiplier, cardiacTolerance) <= 0f;
                    effect.timeleft = CardiacBaselineDurationHours;
                    break;
                case EffectCluster.NEUROTOXICPOISON:
                    /*
                     * NEUROTOXIC POISON :
                     *     - used to indicate nervebane aka nerve damage
                     *     - ladder rung comes from strength left after tolerance - see
                     *       NeurotoxicRungForStrength and ApplyNeurotoxicLadder
                     *     - rung 1: Weakness (walkspeed penalty), 6h
                     *     - rung 2: Paralysis, 12h
                     *     - rung 3: Cardiac Poison's package (flat HP hit, movement/tool
                     *       debuffs, exertion-stacking), 24h
                     *     - walkspeed and headache (rangedWeaponsAcc) penalties and dizziness
                     *       (DrunkWobbleStrength) scale with strength left after tolerance
                     *     - no strength left: the exposure never happens
                     *     - surviving this awards 1/27 of progression towards neurotoxicTolerance
                     */
                    poison = true;
                    negated = StrengthAfterTolerance(effect.effectMultiplier, neurotoxicTolerance) <= 0f;
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
                     *     - hunger rate rises by 100% per point of strength left after tolerance
                     *     - no DoT, no max-health modifier - deliberately non-lethal
                     *     - fixed 24h lifespan
                     *     - no strength left: the exposure never happens
                     *     - surviving this awards 1/27 of progression towards brainrotTolerance
                     */
                    poison = true;
                    negated = StrengthAfterTolerance(effect.effectMultiplier, brainrotTolerance) <= 0f;
                    effect.timeleft = MindPoisonBaselineDurationHours;
                    break;
                case EffectCluster.TOPICALOINTMENT:
                    break;
            }
            bool remedy = PotionEffects.IsRemedyCluster(effect.cluster);
            if (remedy)
            {
                effect.timestarted = entity.World.Calendar.TotalHours;
                effect.timeleft = PotionEffects.DoseDurationFor(effect.cluster);
            }

            //write to treeArrayAttribute
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectname", effectname);
            neweffect.SetString("cluster", effect.cluster.ToString());
            neweffect.SetDouble("timestarted", effect.timestarted);
            neweffect.SetDouble("timeleft", effect.timeleft);
            neweffect.SetFloat("effectMultiplier", effect.effectMultiplier);
            neweffect.SetFloat("toxicEffectMultiplier", effect.toxicEffectMultiplier);
            neweffect.SetFloat("toxicOnsetMultiplier", effect.toxicOnsetMultiplier);
            if (poison)
            {
                if (!IsPostAntidoteWindowActive() && !negated)
                {
                    List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
                    bool toleranceEligible = !forceIneligibleForTolerance
                        && !rrpoisons.Any(existing => existing.GetString("cluster") == effect.cluster.ToString());
                    neweffect.SetBool("toleranceEligible", toleranceEligible);
                    neweffect.SetBool("isPoison", true);
                    neweffect.SetFloat("onsetMultiplier", effect.onsetMultiplier); //only used for poisons

                    if (effect.cluster == EffectCluster.NEUROTOXICPOISON)
                    {
                        ApplyNeurotoxicLadder(neweffect, effect);
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
                    neweffect.SetBool("isPoison", false);
                    // Read by PotionEffects (client and server) for expiry and by /rrPotion list for time remaining.
                    neweffect.SetDouble("absoluteEndTotalHours", effect.timestarted + effect.timeleft);
                    if (remedy)
                    {
                        ApplyRemedyDose(effect, neweffect);
                    }
                    else
                    {
                        AddPotionRecord(neweffect);
                    }
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
                    // Liver Failure from the bushmeat toxicity meter is likewise its own directly
                    // applied DoT, not a tracked thread.
                    if (entity is EntityAgent antidoteTarget)
                    {
                        entity.GetBehavior<EntityBehaviorLiverDisease>()?.CureLiverFailure(antidoteTarget);
                    }
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

        // Accumulated toxin load from discolored meat; reaching FoodPoisoningThreshold causes food
        // poisoning and resets it to zero.
        private const string DiscoloredMeatToxinsKey = "remedyandruinDiscoloredMeatToxins";
        private const double FoodPoisoningThreshold = 1.0;

        /// <summary>
        /// Called once per completed eating action (one plain item, or one meal regardless of
        /// servings) that involves discolored meat. Adds a random amount between 0 and 1 to the
        /// player's persisted toxin total; when the total reaches FoodPoisoningThreshold the
        /// player gets food poisoning and the total resets to zero, so the first meals may do
        /// nothing while the toxins build up. Server side only.
        /// </summary>
        public void OnDiscoloredMeatEaten()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            double toxins = entity.WatchedAttributes.GetDouble(DiscoloredMeatToxinsKey, 0.0) + entity.World.Rand.NextDouble();
            bool poisoned = toxins >= FoodPoisoningThreshold;
            double stored = poisoned ? 0.0 : toxins;
            entity.WatchedAttributes.SetDouble(DiscoloredMeatToxinsKey, stored);
            entity.WatchedAttributes.MarkPathDirty(DiscoloredMeatToxinsKey);
            if (poisoned)
            {
                TriggerFoodPoisoning();
            }

            if (Remedy_And_RuinModSystem.Config?.debugMode == true
                && entity is EntityPlayer player && player.Player is Vintagestory.API.Server.IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(
                    Vintagestory.API.Config.GlobalConstants.InfoLogChatGroup,
                    "Discolored meat eaten: toxins " + toxins.ToString("0.00") + " of " + FoodPoisoningThreshold.ToString("0.00")
                        + (poisoned ? ", food poisoning triggered and toxins reset" : ", no effect yet"),
                    EnumChatType.Notification);
            }
        }

        /// <summary>
        /// Gives the player food poisoning, the 24 in-game hour gastrointestinal illness owned by
        /// EntityBehaviorUpsetStomach. No effect while they already have it.
        /// </summary>
        public void TriggerFoodPoisoning()
        {
            entity.GetBehavior<EntityBehaviorUpsetStomach>()?.StartFoodPoisoning();
        }

        //============== REMEDY DOSE HANDLERS ==============//

        /// <summary>
        /// Routes one valid remedy dose to its cluster's handler. Every handler is called exactly
        /// once per dose, after the dose's record already exists in rrpotions, with the dose's own
        /// tier (Regular or Concentrated, never None). A Diluted dose and a dose voided by the
        /// post-Antidote window never reach here.
        /// </summary>
        private void DispatchRemedyDose(EffectCluster cluster, PotionLevel level)
        {
            switch (cluster)
            {
                case EffectCluster.ANALGESIC: OnAnalgesicDose(level); break;
                case EffectCluster.ANTINAUSEA: OnAntinauseaDose(level); break;
                case EffectCluster.ANTISEPTIC: OnAntisepticDose(level); break;
                case EffectCluster.ANTIVIRAL: OnAntiviralDose(level); break;
                case EffectCluster.MINDTONIC: OnMindTonicDose(level); break;
                case EffectCluster.SEDATIVE: OnSedativeDose(level); break;
                case EffectCluster.TONIC: OnTonicDose(level); break;
                case EffectCluster.TOPICALOINTMENT: OnTopicalOintmentDose(level); break;
            }
        }

        /// <summary>
        /// Applies one valid (neither Diluted nor voided) remedy drink. A cluster with no unexpired
        /// record opens its dose window with a new record, and a drink of more than one 0.25 L
        /// dose overdoses in that same event. A drink while the cluster's window is open is an
        /// overdose, unless the cluster is already overdosed, which makes it a no-op. The Tonic
        /// cannot overdose: another dose restarts its window.
        /// </summary>
        private void ApplyRemedyDose(EffectStruct effect, TreeAttribute newRecord)
        {
            PotionLevel level = PotionEffects.LevelFromEffectMultiplier(effect.effectMultiplier);
            List<TreeAttribute> existing = PotionEffects.ActiveRecords(entity, effect.cluster)
                .OrderBy(r => r.GetDouble("timestarted"))
                .ToList();

            if (effect.cluster == EffectCluster.TONIC)
            {
                if (existing.Count > 0)
                {
                    threadManager.EndPotionRecords(existing.Select(r => r.GetString("effectname")));
                }
                AddPotionRecord(newRecord);
                DispatchRemedyDose(effect.cluster, level);
                return;
            }

            if (existing.Count > 0)
            {
                if (existing.Any(r => r.GetBool(PotionEffects.OverdosedKey))) return;

                existing[0].SetBool(PotionEffects.OverdosedKey, true);
                RRPotionEffects = new TreeArrayAttribute(RRPotionEffects.value);
                TriggerOverdose(effect.cluster, level);
                return;
            }

            bool overdoseInOneGo = effect.doseCount > 1;
            if (overdoseInOneGo)
            {
                newRecord.SetBool(PotionEffects.OverdosedKey, true);
            }
            AddPotionRecord(newRecord);
            DispatchRemedyDose(effect.cluster, level);
            if (overdoseInOneGo)
            {
                TriggerOverdose(effect.cluster, level);
            }
        }

        private void AddPotionRecord(TreeAttribute record)
        {
            List<TreeAttribute> rrpotions = RRPotionEffects.value.ToList<TreeAttribute>();
            rrpotions.Add(record);
            RRPotionEffects = new TreeArrayAttribute(rrpotions.ToArray());
        }

        /// <summary>
        /// Routes one overdose event to its cluster's handler. Called exactly once per overdose,
        /// after the potion's record carries the overdosed flag, with the tier of the drink that
        /// caused it. A multi-dose gulp is still one event.
        /// </summary>
        private void TriggerOverdose(EffectCluster cluster, PotionLevel level)
        {
            switch (cluster)
            {
                case EffectCluster.ANALGESIC: OnAnalgesicOverdose(level); break;
                case EffectCluster.ANTINAUSEA: OnAntinauseaOverdose(level); break;
                case EffectCluster.ANTISEPTIC: OnAntisepticOverdose(level); break;
                case EffectCluster.ANTIVIRAL: OnAntiviralOverdose(level); break;
                case EffectCluster.MINDTONIC: OnMindTonicOverdose(level); break;
                case EffectCluster.SEDATIVE: OnSedativeOverdose(level); break;
                case EffectCluster.TONIC: OnTonicOverdose(level); break;
                case EffectCluster.TOPICALOINTMENT: OnTopicalOintmentOverdose(level); break;
            }
        }

        // Sets the liver toxicity meter to Liver Disease's threshold unless it is already higher.
        private void OnAnalgesicOverdose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server || !(entity is EntityAgent agent)) return;

            entity.GetBehavior<EntityBehaviorLiverDisease>()?.RaiseMeterTo(agent, EntityBehaviorLiverDisease.LiverDiseaseThreshold);
        }
        private void OnAntinauseaOverdose(PotionLevel level)
        {
            entity.GetBehavior<EntityBehaviorUpsetStomach>()?.VoidCompletely();
        }

        private void OnAntisepticOverdose(PotionLevel level)
        {
            entity.GetBehavior<EntityBehaviorUpsetStomach>()?.TriggerUpsetStomach(completeVoid: true);
        }

        private void OnAntiviralOverdose(PotionLevel level) { }
        // Nothing happens while the dose lasts. The Hallucination is due the moment the overdosed
        // record's window ends: the due hour is recorded here and PollOverdoseHallucination starts
        // the episode once the calendar passes it.
        private void OnMindTonicOverdose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            SyncPendingHallucinationToOverdoseEnd();
        }

        // Points the pending Hallucination at the end hour of the active overdosed Mind Tonic
        // record. Re-run every second while that record exists, so a record whose end hour is
        // recomputed (effects that pause while offline) keeps the pending hour matched to it.
        private void SyncPendingHallucinationToOverdoseEnd()
        {
            TreeAttribute overdosed = PotionEffects.ActiveRecords(entity, EffectCluster.MINDTONIC)
                .FirstOrDefault(r => r.GetBool(PotionEffects.OverdosedKey) && r.HasAttribute("absoluteEndTotalHours"));
            if (overdosed == null) return;

            double dueHours = overdosed.GetDouble("absoluteEndTotalHours");
            if (PotionEffects.PendingHallucinationHours(entity) == dueHours) return;

            entity.WatchedAttributes.SetDouble(PotionEffects.PendingHallucinationKey, dueHours);
        }

        // Representative severities for the three Hallucination windows (Drifter only, plus Shiver,
        // plus Bowtorn); one is rolled per episode.
        private static readonly float[] HallucinationWindowSeverities = { 0.2f, 0.5f, 0.85f };

        // Starts the pending overdose Hallucination once its due hour has passed and the player is
        // awake and alive: a full in-game hour from that moment, with a randomly rolled window. A
        // player asleep or offline at the due hour gets it on waking or on the first tick after
        // login. The pending hour is cleared as the episode starts, so it starts exactly once.
        private void PollOverdoseHallucination()
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;

            SyncPendingHallucinationToOverdoseEnd();

            double? due = PotionEffects.PendingHallucinationHours(entity);
            if (!due.HasValue) return;

            double now = entity.World.Calendar.TotalHours;
            if (now < due.Value) return;
            if (entity.GetBehavior<EntityBehaviorTiredness>()?.IsSleeping == true) return;

            entity.WatchedAttributes.RemoveAttribute(PotionEffects.PendingHallucinationKey);
            entity.WatchedAttributes.SetFloat(PotionEffects.HallucinationEpisodeSeverityKey, HallucinationWindowSeverities[entity.World.Rand.Next(HallucinationWindowSeverities.Length)]);
            entity.WatchedAttributes.SetDouble(PotionEffects.HallucinationEpisodeEndKey, now + PotionEffects.HallucinationEpisodeHours);
        }

        // Drops the pending and running overdose Hallucination (death, progress reset).
        private void ClearOverdoseHallucination()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            entity.WatchedAttributes.RemoveAttribute(PotionEffects.PendingHallucinationKey);
            if (entity.WatchedAttributes.HasAttribute(PotionEffects.HallucinationEpisodeEndKey))
            {
                // Zeroed rather than removed so the change reaches the client.
                entity.WatchedAttributes.SetFloat(PotionEffects.HallucinationEpisodeSeverityKey, 0f);
                entity.WatchedAttributes.SetDouble(PotionEffects.HallucinationEpisodeEndKey, 0.0);
            }
        }

        // The bed hold is derived from the overdosed record (Patch_SedativeBedHold), so there is
        // nothing to apply here.
        private void OnSedativeOverdose(PotionLevel level) { }
        private void OnTonicOverdose(PotionLevel level) { }
        // The 50% satiety cut is derived from the overdosed record (PotionEffects.TopicalSatietyMultiplier).
        private void OnTopicalOintmentOverdose(PotionLevel level) { }

        // Per-cluster dose handlers. Contract: called once per valid dose, after the dose's record
        // exists in rrpotions, with that dose's PotionLevel.
        // The Analgesic's effects are derived from its dose record (PotionEffects.PainScale and
        // WalkPenaltyScale); this only re-applies the stat modifiers that depend on them at once.
        private void OnAnalgesicDose(PotionLevel level)
        {
            RefreshAnalgesicScales(force: true);
        }
        private void OnAntinauseaDose(PotionLevel level)
        {
            entity.GetBehavior<EntityBehaviorUpsetStomach>()?.OnAntinauseaDrunk(level);
        }

        // A 1 L gulp reaches here with its record already flagged overdosed; the overdose handler
        // then supplies the one complete-void episode, so the ordinary dose adds none.
        private void OnAntisepticDose(PotionLevel level)
        {
            if (PotionEffects.IsOverdosed(entity, EffectCluster.ANTISEPTIC)) return;

            entity.GetBehavior<EntityBehaviorUpsetStomach>()?.TriggerUpsetStomach();
        }

        // Regular reverses the Chest Cold family illness one stage; Concentrated cures the current
        // stage outright. Either resets the morning-check exposure window (SetStage opens a new one).
        private void OnAntiviralDose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server || !(entity is EntityAgent agent)) return;

            EntityBehaviorChestColdFamily chestCold = entity.GetBehavior<EntityBehaviorChestColdFamily>();
            if (chestCold == null || chestCold.CurrentStage == EntityBehaviorChestColdFamily.Stage.None) return;

            if (level == PotionLevel.Concentrated) chestCold.CureCurrentStage(agent);
            else chestCold.ReverseOneStage(agent);
        }

        // Both tiers cure Mind Poison outright: every Mind Poison timer is force-ended with no
        // tolerance credit, which also tears down its fog/sway severity, psychedelic hold, hunger
        // modifier and vomit watcher. Other poisons (including the Toxic Poison a mushroom may
        // carry) and all potion records are untouched. The Mind Tonic's scale on mental fog is
        // derived from its dose record (PotionEffects.MindScale).
        private void OnMindTonicDose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            const string mindPoisonCluster = "MINDPOISON";
            threadManager.HandleForcefulEnd(poisonCluster: mindPoisonCluster);
            if (RRPoisonEffects.value.Any(r => r.GetString("cluster") == mindPoisonCluster))
            {
                RRPoisonEffects = new TreeArrayAttribute(RRPoisonEffects.value.Where(r => r.GetString("cluster") != mindPoisonCluster).ToArray());
            }
        }
        // Either tier raises Tiredness to its maximum so the bed accepts the player at once, and
        // marks the coming sleep as sedative-assisted. Grogginess (regular tier only) and the
        // overdose bed hold are derived from the dose record (PotionEffects.IsGroggy,
        // Patch_SedativeBedHold).
        private void OnSedativeDose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;

            EntityBehaviorTiredness tiredness = entity.GetBehavior<EntityBehaviorTiredness>();
            if (tiredness == null) return;

            tiredness.Tiredness = entity.World.Calendar.HoursPerDay / 2f;
            entity.WatchedAttributes.SetBool(SleepCompletionEvents.SedativeAssistedSleepAttributeKey, true);
        }

        // The sedative-assisted flag is consumed when the sleep it set up ends. A flag whose dose
        // has expired with no sleep in progress belongs to a sleep that never happened, so it is
        // dropped to keep a later ordinary sleep from reading as sedative-assisted.
        private void ClearStaleSedativeAssistedFlag()
        {
            if (!entity.WatchedAttributes.HasAttribute(SleepCompletionEvents.SedativeAssistedSleepAttributeKey)) return;
            if (entity.GetBehavior<EntityBehaviorTiredness>()?.IsSleeping == true) return;
            if (PotionEffects.ActivePotionLevel(entity, EffectCluster.SEDATIVE) != PotionLevel.None) return;

            entity.WatchedAttributes.RemoveAttribute(SleepCompletionEvents.SedativeAssistedSleepAttributeKey);
        }

        /// <summary>HP a Regular Tonic dose heals; a Concentrated dose heals twice this.</summary>
        private const float TonicHealPerTier = 4f;

        // Heals through the same Heal damage path vanilla healing items use, scaled by the
        // drinker's healingeffectivness (never below 0). Vanilla's heal clamps to max health. The
        // contagion protection is derived from the dose record (PotionEffects.ContagionChanceMultiplier).
        private void OnTonicDose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;

            float heal = level == PotionLevel.Concentrated ? TonicHealPerTier * 2f : TonicHealPerTier;
            heal *= System.Math.Max(0f, entity.Stats.GetBlended("healingeffectivness"));
            if (heal <= 0f) return;

            entity.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Heal
            }, heal);
        }

        // Starts the drunk satiety cut: its own end hour, kept apart from the dose window.
        // Patch_LiverDiseaseSaturationPenalty derives the multiplier from it.
        private void OnTopicalOintmentDose(PotionLevel level)
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;

            entity.WatchedAttributes.SetDouble(PotionEffects.TopicalSatietyCutEndKey, entity.World.Calendar.TotalHours + PotionEffects.TopicalSatietyCutHours);
        }

        /// <summary>
        /// In-game hours left on a potion dose record, read from its live timer when one exists and
        /// otherwise from the record's own end hour or stored remainder. Never negative.
        /// </summary>
        public double GetPotionRemainingHours(TreeAttribute record)
        {
            double now = entity.World.Calendar.TotalHours;
            string effectname = record.GetString("effectname");
            string guid = effectname.Substring(effectname.IndexOf('|') + 1);

            double end;
            if (!threadManager.TryGetEndTotalHours(guid, out end))
            {
                end = record.HasAttribute("absoluteEndTotalHours")
                    ? record.GetDouble("absoluteEndTotalHours")
                    : now + record.GetDouble("timeleft");
            }
            return Math.Max(0.0, end - now);
        }

        private void ApplyAntidoteAftermathEffect()
        {
            Guid uid = Guid.NewGuid();
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectname", "ANTIDOTEAFTERMATH|" + uid.ToString());
            neweffect.SetString("cluster", "ANTIDOTEAFTERMATH");
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
            VoidStomach(1.0);
            //TODO: wire in chance for additional void events if not triggered by antidote
        }

        /// <summary>
        /// Vomits: cancels the player's active hand action, then drains the given fraction (0 to 1)
        /// of current saturation. A fraction of 1 is a complete void and also empties the Expanded
        /// Stomach meter; a smaller fraction scales that meter down by the same share.
        /// </summary>
        public void VoidStomach(double fraction)
        {
            fraction = Math.Clamp(fraction, 0.0, 1.0);
            InterruptActiveAction();

            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if (hunger == null) return;

            float amountToDrain = (float)(hunger.Saturation * fraction);
            var stomach = entity.GetBehavior("expandedstomach");
            var meterProperty = stomach?.GetType().GetProperty("ExpandedStomachMeter");
            float? meterBefore = meterProperty?.GetValue(stomach) as float?;
            if (meterBefore.HasValue)
            {
                meterProperty.SetValue(stomach, 0f); //drain stomach first so it doesn't intercept the next call
            }
            hunger.Saturation -= amountToDrain;
            if (meterBefore.HasValue && fraction < 1.0)
            {
                meterProperty.SetValue(stomach, (float)(meterBefore.Value * (1.0 - fraction)));
            }
        }

        // Cancels the held-item use in progress (eating, drawing a bow, a tool) without running its
        // stop handler, so an interrupted meal is never completed.
        private void InterruptActiveAction()
        {
            if (entity.World.Side != EnumAppSide.Server || entity is not EntityAgent agent) return;
            if (agent.Controls.HandUse == EnumHandInteract.None) return;

            ItemSlot slot = agent.RightHandItemSlot;
            if (slot?.Itemstack != null)
            {
                float secondsUsed = (entity.World.ElapsedMilliseconds - agent.Controls.UsingBeginMS) / 1000f;
                slot.Itemstack.Collectible.OnHeldUseCancel(secondsUsed, slot, agent, null, null, EnumItemUseCancelReason.MovedAway);
            }
            agent.Controls.HandUse = EnumHandInteract.None;
            agent.AnimManager?.StopAnimation("eat");
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
