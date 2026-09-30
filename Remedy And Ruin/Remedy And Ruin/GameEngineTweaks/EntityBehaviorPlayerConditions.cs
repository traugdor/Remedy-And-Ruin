using System;
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
            entity.GetBehavior<EntityBehaviorHealth>()?.StopDoTEffect((int)EnumDamageOverTimeEffectType.Bleeding);
        }
    }
}
