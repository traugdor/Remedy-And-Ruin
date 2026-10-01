using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Owns Bleeding, Wound Infection, and Skin Irritation state - the combat/environment-triggered
    /// physical conditions from 02-design-overview.md's Part 3, distinct from
    /// EntityBehaviorRemedyEffects' eaten/drunk poison-cluster pipeline. Attached to every entity
    /// with EntityBehaviorHealth on spawn (see Remedy And RuinModSystem.StartServerSide) since
    /// wildlife can bleed too, even though only players currently have any way to treat these
    /// conditions.
    ///
    /// ApplySkinIrritation: applies the healingeffectivness debuff and (re)starts its self-resolve
    /// timer - re-applying while already active just restarts the timer, it doesn't stack. Trigger
    /// detection (frostbite/sunburn/irritant-plant contact) isn't implemented here; call this from
    /// wherever that later gets built.
    ///
    /// ClearSkinIrritation: clears Skin Irritation instantly regardless of tier - applying a
    /// treated bandage (Topical Ointment, any potency) does this per 02-design-overview.md:1192-1194,
    /// which makes no Concentrated-vs-regular distinction for this condition.
    ///
    /// ApplyBleeding: starts (or replaces, if stronger) a Bleeding DoT at the given tier and returns
    /// the actual instant damage the caller should apply in place of the original hit, since half
    /// of it becomes this DoT instead of landing immediately.
    ///
    /// StaunchBleeding: stops the active Bleeding DoT outright - any healing item's completed
    /// application calls this (staunching is universal to any bandage/poultice), regardless of
    /// whether it also cures Wound Infection (a later task).
    /// </summary>
    public class EntityBehaviorPlayerConditions : EntityBehavior
    {
        // -5% healingeffectivness (02-design-overview.md:1188-1190) - the same stat category Wound
        // Infection and the Chest Cold family's later stages use, at the mildest magnitude in the
        // catalog.
        private const float SkinIrritationHealingEffectivnessPenalty = -0.05f;
        private const string SkinIrritationStatKey = "remedyandruinSkinIrritation";

        // Self-resolves in 2-3 in-game days if untreated (02-design-overview.md:1191) - midpoint of
        // that range.
        private const double SkinIrritationSelfResolveHours = 2.5 * 24.0;

        private long skinIrritationSelfResolveListenerId;

        public EntityBehaviorPlayerConditions(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinPlayerConditions";

        public bool HasSkinIrritation => entity.WatchedAttributes.GetBool("remedyandruinSkinIrritation", false);

        public void ApplySkinIrritation(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            target.WatchedAttributes.SetBool("remedyandruinSkinIrritation", true);
            target.Stats.Set("healingeffectivness", SkinIrritationStatKey, SkinIrritationHealingEffectivnessPenalty, persistent: true);

            EntityBehaviorPlayerConditions conditions = target.GetBehavior<EntityBehaviorPlayerConditions>();
            if (conditions == null) return;

            if (conditions.skinIrritationSelfResolveListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(conditions.skinIrritationSelfResolveListenerId);
            }
            conditions.skinIrritationSelfResolveListenerId = target.World.RegisterCallback(
                dt => conditions.ClearSkinIrritation(target),
                (int)(SkinIrritationSelfResolveHours * CalendarTimeHelper.RealSecondsPerGameHour(target.World.Calendar) * 1000.0));
        }

        public void ClearSkinIrritation(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;
            if (!target.WatchedAttributes.GetBool("remedyandruinSkinIrritation", false)) return;

            target.WatchedAttributes.SetBool("remedyandruinSkinIrritation", false);
            target.Stats.Remove("healingeffectivness", SkinIrritationStatKey);

            EntityBehaviorPlayerConditions conditions = target.GetBehavior<EntityBehaviorPlayerConditions>();
            if (conditions != null && conditions.skinIrritationSelfResolveListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(conditions.skinIrritationSelfResolveListenerId);
                conditions.skinIrritationSelfResolveListenerId = 0L;
            }
        }

        // Minor <4 damage taken, Moderate 4-8, Severe >8 - calibrated against damage actually taken after
        // armor mitigation, which is already what reaches onDamaged by the time this reads it (vanilla's
        // own armor reduction runs earlier in the pipeline).
        private const float BleedingModerateThreshold = 4f;
        private const float BleedingSevereThreshold = 8f;

        private const float BleedingMinorHpPerSec = 0.1f;
        private const float BleedingMinorDurationSec = 60f;
        private const float BleedingModerateHpPerSec = 0.25f;
        private const float BleedingModerateDurationSec = 60f;
        private const float BleedingSevereHpPerSec = 0.5f;
        private const float BleedingSevereDurationSec = 120f;

        public enum BleedingTier { Minor, Moderate, Severe }

        public static BleedingTier? DetermineBleedingTier(float damageTaken)
        {
            if (damageTaken >= BleedingSevereThreshold) return BleedingTier.Severe;
            if (damageTaken >= BleedingModerateThreshold) return BleedingTier.Moderate;
            if (damageTaken > 0f) return BleedingTier.Minor;
            return null;
        }

        public static (float hpPerSec, float durationSec) BleedingTierRates(BleedingTier tier) => tier switch
        {
            BleedingTier.Minor => (BleedingMinorHpPerSec, BleedingMinorDurationSec),
            BleedingTier.Moderate => (BleedingModerateHpPerSec, BleedingModerateDurationSec),
            _ => (BleedingSevereHpPerSec, BleedingSevereDurationSec)
        };

        public float ApplyBleeding(BleedingTier newTier, float fullDamage, DamageSource damageSource)
        {
            EntityBehaviorHealth health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return fullDamage;

            BleedingTier? existingTier = CurrentBleedingTier(health);
            if (existingTier.HasValue && existingTier.Value >= newTier) return fullDamage;

            if (existingTier.HasValue)
            {
                health.StopDoTEffect((int)EnumDamageOverTimeEffectType.Bleeding);
            }

            const float instantDamageFraction = 0.5f;
            float instantDamage = fullDamage * instantDamageFraction;
            float bleedPortion = fullDamage - instantDamage;

            (float hpPerSec, float durationSec) = BleedingTierRates(newTier);
            int ticks = (int)durationSec; // 1 tick/sec

            // ProcessDoTEffects rebuilds a bare per-tick DamageSource with no CauseEntity/
            // SourceEntity. Passing the attacker's own EnumDamageSource.Entity here would make
            // EntityPlayer.OnHurt's GetCauseEntity() call throw on that null every tick, aborting
            // OnEntityReceiveDamage before its Health<=0 death check and before TicksLeft/
            // PreviousTickTime advance - so the bleed would never expire and health could go
            // negative without ever killing the player. Bleed avoids every OnHurt branch that
            // touches GetCauseEntity(), and also marks each tick so Patch_SuppressBleedingSound can
            // silence its repeated hurt grunt without touching the original bite's own sound.
            health.ApplyDoTEffect(EnumDamageSource.Bleed, damageSource.Type, damageSource.DamageTier,
                hpPerSec * durationSec, TimeSpan.FromSeconds(durationSec), ticks, EnumDamageOverTimeEffectType.Bleeding);

            entity.WatchedAttributes.SetInt("remedyandruinBleedingTier", (int)newTier);
            StartWoundInfectionRisk((EntityAgent)entity, newTier);

            if (entity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer serverPlayer)
            {
                string tierName = newTier.ToString().ToLowerInvariant();
                serverPlayer.SendIngameError("remedyandruinBleeding", $"That gave you a {tierName} bleed! Bandage it quick!");
            }

            return instantDamage;
        }

        private BleedingTier? CurrentBleedingTier(EntityBehaviorHealth health)
        {
            foreach (DamageOverTimeEffect effect in health.ActiveDoTEffects)
            {
                if (effect.EffectType == (int)EnumDamageOverTimeEffectType.Bleeding)
                {
                    return (BleedingTier)entity.WatchedAttributes.GetInt("remedyandruinBleedingTier", 0);
                }
            }
            return null;
        }

        public void StaunchBleeding()
        {
            if (entity.World.Side != EnumAppSide.Server) return;
            StopWoundInfectionRisk((EntityAgent)entity);
            entity.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect((int)EnumDamageOverTimeEffectType.Bleeding);
        }

        // Base chance by the Bleeding tier that caused it, rolled every 30s the wound stays unbandaged,
        // +5% per roll, capped at +50% total.
        private static readonly Dictionary<BleedingTier, double> WoundInfectionBaseChance = new Dictionary<BleedingTier, double>
        {
            { BleedingTier.Minor, 0.05 },
            { BleedingTier.Moderate, 0.15 },
            { BleedingTier.Severe, 0.35 }
        };
        private const double WoundInfectionRollIncrement = 0.05;
        private const double WoundInfectionRollCap = 0.50; // added on top of the base chance, not an absolute ceiling on the roll itself
        private const int WoundInfectionRollIntervalMs = 30000;

        // 0.05 HP/3s = ~1 HP/min. Never self-resolves.
        private const float WoundInfectionDoTPerTick = 0.05f;
        private const int WoundInfectionDoTTickSeconds = 3;
        internal const int WoundInfectionDoTEffectType = -19343; // mod-defined, distinct from ArrowBonusToxicDoTEffectType (-19342) and vanilla's Poison/Bleeding

        // -15% healingeffectivness, -10% walkspeed while infected.
        private const float WoundInfectionHealingEffectivnessPenalty = -0.15f;
        private const float WoundInfectionWalkSpeedPenalty = -0.10f;
        private const string WoundInfectionHealStatKey = "remedyandruinWoundInfectionHeal";
        private const string WoundInfectionWalkStatKey = "remedyandruinWoundInfectionWalk";

        // Debuff taper after a regular-Antiseptic cure - 3 in-game hours. Concentrated Antiseptic clears
        // both the infection and this debuff instantly instead - see CureWoundInfection.
        private const double WoundInfectionDebuffTaperHours = 3.0;

        private long woundInfectionRollListenerId;
        private int woundInfectionRollCount;

        public bool HasWoundInfection => entity.WatchedAttributes.GetBool("remedyandruinWoundInfection", false);

        /// <summary>
        /// Called whenever a new Bleeding DoT actually starts (not on a weaker hit that got dropped) -
        /// starts the periodic infection-chance roll for as long as the wound stays unbandaged. A fresh
        /// call while a roll timer is already running restarts it at the new tier's base chance, matching
        /// "the wound" being singular (Bleeding itself doesn't stack, so neither does its infection risk).
        /// </summary>
        public void StartWoundInfectionRisk(EntityAgent target, BleedingTier bleedingTier)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            EntityBehaviorPlayerConditions conditions = target.GetBehavior<EntityBehaviorPlayerConditions>();
            if (conditions == null) return;

            if (conditions.woundInfectionRollListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(conditions.woundInfectionRollListenerId);
            }
            conditions.woundInfectionRollCount = 0;

            double baseChance = WoundInfectionBaseChance[bleedingTier];
            var rand = new System.Random();
            conditions.woundInfectionRollListenerId = target.World.RegisterGameTickListener(dt =>
            {
                if (conditions.HasWoundInfection)
                {
                    target.World.UnregisterGameTickListener(conditions.woundInfectionRollListenerId);
                    conditions.woundInfectionRollListenerId = 0L;
                    return;
                }
                double chance = System.Math.Min(baseChance + WoundInfectionRollCap, baseChance + conditions.woundInfectionRollCount * WoundInfectionRollIncrement);
                conditions.woundInfectionRollCount++;
                if (rand.NextDouble() < chance)
                {
                    conditions.ApplyWoundInfection(target);
                }
            }, WoundInfectionRollIntervalMs);
        }

        /// <summary>
        /// Called when Bleeding's own DoT ends (naturally or via staunching) - stops the infection-risk
        /// roll. Does not clear an infection that already took hold; that only happens via
        /// CureWoundInfection.
        /// </summary>
        public void StopWoundInfectionRisk(EntityAgent target)
        {
            if (woundInfectionRollListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(woundInfectionRollListenerId);
                woundInfectionRollListenerId = 0L;
            }
        }

        public void ApplyWoundInfection(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            target.WatchedAttributes.SetBool("remedyandruinWoundInfection", true);
            target.Stats.Set("healingeffectivness", WoundInfectionHealStatKey, WoundInfectionHealingEffectivnessPenalty, persistent: true);
            target.Stats.Set("walkspeed", WoundInfectionWalkStatKey, WoundInfectionWalkSpeedPenalty, persistent: true);

            // Reuses EffectThreadManager's own "runs until explicitly stopped" DoT builder (1000 real days,
            // sized so the per-tick math reproduces an exact damage-per-second rate) rather than re-deriving
            // the same math here.
            float damagePerSecond = WoundInfectionDoTPerTick / WoundInfectionDoTTickSeconds;
            DoTSpec spec = EffectThreadManager.BuildEffectivelyForeverDoT(EnumDamageSource.Internal, EnumDamageType.Injury, 0, damagePerSecond);
            EntityBehaviorHealth health = target.GetBehavior<EntityBehaviorHealth>();
            health?.ApplyDoTEffect(spec.DamageSource, spec.DamageType, spec.DamageTier, spec.TotalDamage, spec.TotalTime, spec.TicksNumber, WoundInfectionDoTEffectType);
        }

        /// <summary>
        /// Cures Wound Infection and/or its lingering debuff. Regular Antiseptic stops the DoT and
        /// clears the infection flag immediately but leaves the -15%/-10% debuff to taper off over 3
        /// in-game hours; Concentrated clears the debuff instantly too. A Concentrated application
        /// also finishes off an already-tapering debuff from an earlier regular cure - the infection
        /// flag alone isn't enough to gate this, since it's already false during that taper window,
        /// which previously made a later Concentrated application silently no-op.
        /// </summary>
        public void CureWoundInfection(EntityAgent target, bool concentrated)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            bool wasInfected = target.WatchedAttributes.GetBool("remedyandruinWoundInfection", false);
            bool tapering = target.WatchedAttributes.GetBool("remedyandruinWoundInfectionTapering", false);
            if (!wasInfected && !tapering) return;

            if (wasInfected)
            {
                target.WatchedAttributes.SetBool("remedyandruinWoundInfection", false);
                target.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect(WoundInfectionDoTEffectType);
            }

            if (concentrated)
            {
                target.WatchedAttributes.SetBool("remedyandruinWoundInfectionTapering", false);
                target.Stats.Remove("healingeffectivness", WoundInfectionHealStatKey);
                target.Stats.Remove("walkspeed", WoundInfectionWalkStatKey);
                return;
            }

            if (wasInfected && !tapering)
            {
                target.WatchedAttributes.SetBool("remedyandruinWoundInfectionTapering", true);
                TaperWoundInfectionDebuff(target);
            }
        }

        private void TaperWoundInfectionDebuff(EntityAgent target)
        {
            double taperRealSeconds = WoundInfectionDebuffTaperHours * CalendarTimeHelper.RealSecondsPerGameHour(target.World.Calendar);
            target.World.RegisterCallback(dt =>
            {
                target.WatchedAttributes.SetBool("remedyandruinWoundInfectionTapering", false);
                target.Stats.Remove("healingeffectivness", WoundInfectionHealStatKey);
                target.Stats.Remove("walkspeed", WoundInfectionWalkStatKey);
            }, (int)(taperRealSeconds * 1000.0));
        }
    }
}
