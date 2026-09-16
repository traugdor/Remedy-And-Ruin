using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// EntityProjectileBase.DealDamage is where a fired arrow actually lands its hit. A poisoned
    /// arrow (remedyandruinArrowPoisonCluster attribute, set by the dip/craft interactions)
    /// applies its poison directly to the target here, on a successful hit only - never
    /// contributing to tolerance (02-design-overview.md:832-833), and respecting the
    /// post-Antidote immunity window the same way eating a poison does.
    ///
    /// Toxic Poison's coated-arrow effect is its own bespoke mechanic (flat bonus damage plus a
    /// chance of a smaller bonus DoT) rather than the same delayed onset/liver-failure package
    /// drinking uses - see ApplyToxicArrowHit. Noxious Poison's and Cardiac Poison's arrow-hits
    /// are each a chance gate around the same drinking-equivalent package - see
    /// ApplyNoxiousArrowHit/ApplyCardiacArrowHit. Every other cluster still routes through
    /// EntityBehaviorRemedyEffects.ApplyEffect unconditionally, the same path drinking uses,
    /// until its own task replaces this with its real per-cluster chance.
    /// </summary>
    [HarmonyPatch(typeof(EntityProjectileBase), "DealDamage")]
    public static class Patch_ArrowPoisonDelivery
    {
        // Flat, unconditional per hit - 02-design-overview.md's poison-effects table gives this as
        // a fixed amount, not scaled by the arrow's own effectMultiplier.
        private const float ToxicArrowFlatBonusDamage = 1.5f;

        // Half the drink dose's 1.5 HP/sec rate - the design doc asks for "a smaller version" of
        // the liver-failure DoT without giving an exact fraction.
        private const float ToxicArrowSecondaryDoTPerSecond = 0.75f;

        // Within the design doc's suggested 25-35% range for this cluster's secondary-DoT proc.
        private const double ToxicArrowSecondaryDoTChance = 0.30;

        // Within the design doc's "High chance" / 70-80% range - distinctly higher than
        // Mind Poison's 40-55% and Cardiac/Neurotoxic's 25-35%.
        private const double NoxiousArrowHitChance = 0.75;

        // Within the design doc's own stated 25-35% range for this cluster's coated-arrow chance
        // (02-design-overview.md ~1407).
        private const double CardiacArrowHitChance = 0.30;

        public static void Postfix(EntityProjectileBase __instance, Entity target, bool __result)
        {
            if (!__result) return; // no actual hit landed

            ItemStack arrowStack = __instance.ProjectileStack;
            string cluster = arrowStack?.Attributes.GetString("remedyandruinArrowPoisonCluster");
            if (string.IsNullOrEmpty(cluster)) return;

            EntityBehaviorRemedyEffects remedyBehavior = target.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyBehavior == null) return;

            if (cluster == "TOXICPOISON")
            {
                ApplyToxicArrowHit(target, remedyBehavior);
                return;
            }

            if (cluster == "NOXIOUSPOISON")
            {
                ApplyNoxiousArrowHit(remedyBehavior, arrowStack);
                return;
            }

            if (cluster == "CARDIACPOISON")
            {
                ApplyCardiacArrowHit(remedyBehavior, arrowStack);
                return;
            }

            float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
            EntityBehaviorRemedyEffects.EffectStruct effect = new EntityBehaviorRemedyEffects.EffectStruct(cluster.ToEnum<EntityBehaviorRemedyEffects.EffectCluster>())
            {
                isPoison = true,
                effectMultiplier = effectMultiplier
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }

        private static void ApplyToxicArrowHit(Entity target, EntityBehaviorRemedyEffects remedyBehavior)
        {
            if (remedyBehavior.IsPostAntidoteWindowActive()) return; // poison immunity active

            target.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Poison,
                IgnoreInvFrames = true
            }, ToxicArrowFlatBonusDamage);

            if (!target.Alive) return; // the flat bonus alone finished it off - no DoT to inject
            if (new Random().NextDouble() >= ToxicArrowSecondaryDoTChance) return;

            EntityBehaviorHealth health = target.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            DoTSpec spec = EffectThreadManager.BuildEffectivelyForeverDoT(
                EnumDamageSource.Internal, EnumDamageType.Poison,
                EffectThreadManager.ArrowBonusToxicDoTDamageTier, ToxicArrowSecondaryDoTPerSecond);
            health.ApplyDoTEffect(spec.DamageSource, spec.DamageType, spec.DamageTier, spec.TotalDamage, spec.TotalTime, spec.TicksNumber, EffectThreadManager.ArrowBonusToxicDoTEffectType);
        }

        // A missed roll applies nothing at all - not even a partial effect. A successful roll
        // routes through the normal ApplyEffect path (onset delay included), the same as any
        // other still-generic cluster's arrow hit - IsPostAntidoteWindowActive() and tolerance
        // ineligibility are both handled there, same as a drunk dose.
        private static void ApplyNoxiousArrowHit(EntityBehaviorRemedyEffects remedyBehavior, ItemStack arrowStack)
        {
            if (new Random().NextDouble() >= NoxiousArrowHitChance) return;

            float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
            var effect = new EntityBehaviorRemedyEffects.EffectStruct(EntityBehaviorRemedyEffects.EffectCluster.NOXIOUSPOISON)
            {
                isPoison = true,
                effectMultiplier = effectMultiplier
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }

        // A missed roll applies nothing at all - not even a partial effect. A successful roll
        // routes through the normal ApplyEffect path: the same full-phase package (flat HP hit,
        // movement/tool debuffs, exertion-stacking watcher) and onset delay drinking triggers,
        // just gated behind this chance roll first - drinking itself stays ungated (100%,
        // unchanged by this patch).
        private static void ApplyCardiacArrowHit(EntityBehaviorRemedyEffects remedyBehavior, ItemStack arrowStack)
        {
            if (new Random().NextDouble() >= CardiacArrowHitChance) return;

            float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
            var effect = new EntityBehaviorRemedyEffects.EffectStruct(EntityBehaviorRemedyEffects.EffectCluster.CARDIACPOISON)
            {
                isPoison = true,
                effectMultiplier = effectMultiplier
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }
    }

    /// <summary>
    /// Hunting-risk consequence of Toxic Poison's arrow-delivered secondary DoT
    /// (Patch_ArrowPoisonDelivery.ApplyToxicArrowHit): an animal it lands the killing blow on
    /// drops no meat. EntityBehaviorHealth.ProcessDoTEffects rebuilds a bare DamageSource per tick
    /// carrying only Source/Type/DamageTier, so DamageTier is the only field that survives into
    /// Die's damageSourceForDeath and is this DoT's sole identifying mark at the point of death.
    /// </summary>
    [HarmonyPatch(typeof(Entity), "Die")]
    public static class Patch_ArrowToxicDoTHuntingRisk
    {
        public static void Postfix(Entity __instance, EnumDespawnReason reason, DamageSource damageSourceForDeath)
        {
            if (reason != EnumDespawnReason.Death || damageSourceForDeath == null) return;
            if (__instance is EntityPlayer) return;
            if (damageSourceForDeath.Source != EnumDamageSource.Internal) return;
            if (damageSourceForDeath.Type != EnumDamageType.Poison) return;
            if (damageSourceForDeath.DamageTier != EffectThreadManager.ArrowBonusToxicDoTDamageTier) return;

            EntityBehaviorHarvestable harvestable = __instance.GetBehavior<EntityBehaviorHarvestable>();
            if (harvestable == null) return;

            // GenerateDrops (the meat/hide drop table) checks this flag first and no-ops if it's
            // already set - marking it here, before the carcass is ever harvested, permanently
            // suppresses that drop table without touching jsonDrops or the drop-roll logic itself.
            harvestable.DropsGenerated = true;
        }
    }
}
