using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Remedy_And_Ruin.GameEngineTweaks;

namespace Remedy_And_Ruin.RRBehaviors
{
    /// <summary>
    /// Owns Liver Disease and the Liver Failure it escalates into, driven by a dedicated liver
    /// toxicity meter (02-design-overview.md's Part 3). Each completed cooked-bushmeat eat adds
    /// ToxicityPerBushmeat to the meter, which drains at ToxicityDecayPerGameDay per in-game day -
    /// it is stored as a value plus the calendar day it was last written and decayed lazily on
    /// read, so it tracks calendar speed and keeps draining across saves and relogs.
    ///
    /// Liver Disease starts when the meter reaches LiverDiseaseThreshold and Liver Failure when it
    /// reaches LiverFailureThreshold; both end only once the meter has drained back to 0 (or on
    /// death). AddToxicity: the single entry point any other source of liver toxicity (e.g. a
    /// future Tonic overdose) should feed.
    /// </summary>
    public class EntityBehaviorLiverDisease : EntityBehavior
    {
        private const float ToxicityPerBushmeat = 0.25f;
        private const double LiverDiseaseThreshold = 3.0;
        private const double LiverFailureThreshold = 6.0;
        private const double ToxicityDecayPerGameDay = 1.0;
        private const int ClearCheckIntervalMs = 5000;

        private const float LiverDiseaseHealingEffectivnessPenalty = -0.10f;
        private const float LiverDiseaseSaturationGainMultiplier = 0.90f; // -10% saturation gain
        private const float LiverDiseaseMaxHealthPenalty = -2.5f;
        private const string LiverDiseaseHealStatKey = "remedyandruinLiverDiseaseHeal";
        private const string LiverDiseaseMaxHealthKey = "remedyandruinLiverDisease";

        // Applied directly under its own effect type so its lifetime follows the toxicity meter
        // rather than the tolerance-gated poison-cluster pipeline. Uses the (Internal, Injury)
        // damage source so Patch_SuppressConditionTickSound silences its per-tick hurt grunt, the
        // same as Wound Infection's DoT.
        internal const int LiverFailureDoTEffectType = -19344;
        private const float LiverFailureDoTDamagePerSecond = 0.1f;

        private const string ToxicityKey = "remedyandruinLiverToxicity";
        private const string ToxicityDayKey = "remedyandruinLiverToxicityDay";
        private const string LiverDiseaseKey = "remedyandruinLiverDisease";
        private const string LiverFailureKey = "remedyandruinLiverFailure";

        private long clearCheckListenerId;

        public EntityBehaviorLiverDisease(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinLiverDisease";

        public bool HasLiverDisease => entity.WatchedAttributes.GetBool(LiverDiseaseKey, false);

        public bool HasLiverFailure => entity.WatchedAttributes.GetBool(LiverFailureKey, false);

        public double CurrentToxicity
        {
            get
            {
                double stored = entity.WatchedAttributes.GetDouble(ToxicityKey, 0.0);
                double writtenDay = entity.WatchedAttributes.GetDouble(ToxicityDayKey, 0.0);
                double elapsedDays = Math.Max(0.0, entity.World.Calendar.TotalDays - writtenDay);
                return Math.Max(0.0, stored - elapsedDays * ToxicityDecayPerGameDay);
            }
        }

        /// <summary>
        /// Called from Patch_RawEating for every completed eat of an edible item - only cooked
        /// bushmeat adds toxicity.
        /// </summary>
        public void OnFoodConsumed(ItemStack stack, EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;
            if (!(stack?.Collectible?.Code?.Path?.StartsWith("bushmeat-cooked") ?? false)) return;

            AddToxicity(target, ToxicityPerBushmeat);
        }

        public void AddToxicity(EntityAgent target, double amount)
        {
            SetToxicity(target, CurrentToxicity + amount);
        }

        /// <summary>
        /// Writes the meter directly and re-evaluates the thresholds. Setting it to 0 ends both
        /// conditions immediately.
        /// </summary>
        public void SetToxicity(EntityAgent target, double value)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            target.WatchedAttributes.SetDouble(ToxicityKey, Math.Max(0.0, value));
            target.WatchedAttributes.SetDouble(ToxicityDayKey, target.World.Calendar.TotalDays);
            EvaluateThresholds(target);
        }

        /// <summary>
        /// Re-arms the clear check and re-applies the debuffs for a condition that was already
        /// active when the player logged out - stat modifiers persist but the poll listener and the
        /// max-health modifier do not.
        /// </summary>
        public void ResumeAfterLogin(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            if (HasLiverDisease)
            {
                ApplyLiverDiseaseDebuffs(target);
            }
            else
            {
                target.Stats.Remove("healingeffectivness", LiverDiseaseHealStatKey);
                target.GetBehavior<EntityBehaviorHealth>()?.SetMaxHealthModifiers(LiverDiseaseMaxHealthKey, 0f);
            }
            EvaluateThresholds(target);
        }

        private void EvaluateThresholds(EntityAgent target)
        {
            double meter = CurrentToxicity;

            if (meter <= 0.0)
            {
                ClearAll(target);
                return;
            }

            if (meter >= LiverDiseaseThreshold && !HasLiverDisease)
            {
                target.WatchedAttributes.SetBool(LiverDiseaseKey, true);
                ApplyLiverDiseaseDebuffs(target);
            }

            if (meter >= LiverFailureThreshold && !HasLiverFailure)
            {
                target.WatchedAttributes.SetBool(LiverFailureKey, true);
                DoTSpec spec = EffectThreadManager.BuildEffectivelyForeverDoT(EnumDamageSource.Internal, EnumDamageType.Injury, 0, LiverFailureDoTDamagePerSecond);
                target.GetBehavior<EntityBehaviorHealth>()?.ApplyDoTEffect(spec.DamageSource, spec.DamageType, spec.DamageTier, spec.TotalDamage, spec.TotalTime, spec.TicksNumber, LiverFailureDoTEffectType);
            }

            if ((HasLiverDisease || HasLiverFailure) && clearCheckListenerId == 0L)
            {
                clearCheckListenerId = target.World.RegisterGameTickListener(dt =>
                {
                    if (CurrentToxicity <= 0.0) ClearAll(target);
                }, ClearCheckIntervalMs);
            }
        }

        /// <summary>
        /// Antidote's cure for Liver Failure: stops its DoT and drops the meter just under the
        /// Liver Failure threshold, so the cure holds until the next bushmeat pushes it back over.
        /// Liver Disease itself is left to fade with the meter.
        /// </summary>
        public void CureLiverFailure(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server || !HasLiverFailure) return;

            target.WatchedAttributes.SetBool(LiverFailureKey, false);
            target.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect(LiverFailureDoTEffectType);
            SetToxicity(target, Math.Min(CurrentToxicity, LiverFailureThreshold - ToxicityPerBushmeat));
        }

        private void ApplyLiverDiseaseDebuffs(EntityAgent target)
        {
            target.Stats.Set("healingeffectivness", LiverDiseaseHealStatKey, LiverDiseaseHealingEffectivnessPenalty, persistent: true);
            target.GetBehavior<EntityBehaviorHealth>()?.SetMaxHealthModifiers(LiverDiseaseMaxHealthKey, LiverDiseaseMaxHealthPenalty);
        }

        private void ClearAll(EntityAgent target)
        {
            if (clearCheckListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(clearCheckListenerId);
                clearCheckListenerId = 0L;
            }

            if (HasLiverFailure)
            {
                target.WatchedAttributes.SetBool(LiverFailureKey, false);
                target.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect(LiverFailureDoTEffectType);
            }

            if (HasLiverDisease)
            {
                target.WatchedAttributes.SetBool(LiverDiseaseKey, false);
                target.Stats.Remove("healingeffectivness", LiverDiseaseHealStatKey);
                target.GetBehavior<EntityBehaviorHealth>()?.SetMaxHealthModifiers(LiverDiseaseMaxHealthKey, 0f);
            }
        }

        /// <summary>
        /// Death ends both conditions and resets the meter - vanilla clears the entity's DoTs on
        /// death, so leaving the flags set would strand Liver Failure as "active" with no damage.
        /// </summary>
        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side != EnumAppSide.Server || !(entity is EntityAgent agent)) return;
            SetToxicity(agent, 0.0);
        }

        public float ApplySaturationPenalty(float saturation) => HasLiverDisease ? saturation * LiverDiseaseSaturationGainMultiplier : saturation;
    }
}
