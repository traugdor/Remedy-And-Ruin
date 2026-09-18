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
    /// ApplyNoxiousArrowHit/ApplyCardiacArrowHit. Neurotoxic Poison's arrow-hit is a chance gate
    /// around a half-weight ladder contribution rather than a full dose - see
    /// ApplyNeurotoxicArrowHit. Mind Poison's arrow-hit is a chance gate around the same
    /// drinking-equivalent package as Noxious/Cardiac's - see ApplyMindPoisonArrowHit. Every
    /// other cluster still routes through EntityBehaviorRemedyEffects.ApplyEffect
    /// unconditionally, the same path drinking uses, until its own task replaces this with its
    /// real per-cluster chance.
    ///
    /// Non-player targets (anything without EntityBehaviorRemedyEffects) use a separate, much
    /// simpler path: only Liverbane (instant chance-DoT, no antidote-window check since animals
    /// have no antidote system) and Neurotoxic (a permanent, stacking walkspeed reduction via
    /// EntityBehaviorArrowPoisonDelivery.AddNeurotoxicStack) have a defined reaction - every other
    /// cluster no-ops. Constructs (vanilla's "mechanical" tag) are immune to everything; rust
    /// creatures ("rust-creature" tag) are immune to everything except Neurotoxic.
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

        // Within the design doc's own stated 25-35% range, matching Cardiac's own 30% for
        // consistency (02-design-overview.md ~1407, 1431-1443).
        private const double NeurotoxicArrowHitChance = 0.30;

        // Within the design doc's own stated 40-55% range for this cluster - higher than
        // Cardiac/Neurotoxic's 25-35% since Mind Poison is non-lethal by design, less reason to
        // gate it as tightly (02-design-overview.md ~1409).
        private const double MindPoisonArrowHitChance = 0.45;

        // Bloodstream-direct delivery manifests far faster than digestion - 10x normal onset
        // speed (baseline/10 hours, since onset is documented as an inverse-scale speed
        // multiplier). Left unset, an EffectStruct's onsetMultiplier silently defaults to 0,
        // which under EffectThreadManager's divide-by-zero floor computed as 20x SLOWER than
        // baseline instead - every cluster below needs this set explicitly.
        private const float ArrowHitOnsetMultiplier = 10f;

        public static void Postfix(EntityProjectileBase __instance, Entity target, bool __result)
        {
            if (!__result) return; // no actual hit landed

            ItemStack arrowStack = __instance.ProjectileStack;
            string cluster = arrowStack?.Attributes.GetString("remedyandruinArrowPoisonCluster");
            if (string.IsNullOrEmpty(cluster)) return;

            // Constructs (bells, locusts, the Eidolon - vanilla's "mechanical" tag) are immune to
            // every cluster. Rust creatures (Drifter/Shiver/Bowtorn) are immune to everything
            // except Neurotoxic, which works on them the same as any other non-player target.
            EntityBehaviorArrowPoisonDelivery.ImmunityLevel immunity = EntityBehaviorArrowPoisonDelivery.GetImmunityLevel(target);
            if (immunity == EntityBehaviorArrowPoisonDelivery.ImmunityLevel.FullyImmune) return;
            if (immunity == EntityBehaviorArrowPoisonDelivery.ImmunityLevel.NeurotoxicOnly && cluster != "NEUROTOXICPOISON") return;

            EntityBehaviorRemedyEffects remedyBehavior = target.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyBehavior != null)
            {
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

                if (cluster == "NEUROTOXICPOISON")
                {
                    ApplyNeurotoxicArrowHit(remedyBehavior, arrowStack);
                    return;
                }

                if (cluster == "MINDPOISON")
                {
                    ApplyMindPoisonArrowHit(remedyBehavior, arrowStack);
                    return;
                }

                float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
                EntityBehaviorRemedyEffects.EffectStruct effect = new EntityBehaviorRemedyEffects.EffectStruct(cluster.ToEnum<EntityBehaviorRemedyEffects.EffectCluster>())
                {
                    isPoison = true,
                    effectMultiplier = effectMultiplier,
                    onsetMultiplier = ArrowHitOnsetMultiplier
                };
                remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
                return;
            }

            // Non-player path: only Liverbane and Neurotoxic have a defined reaction (per
            // docs/plans/future-work-ideas.md) - every other cluster no-ops here, same as immunity.
            EntityBehaviorArrowPoisonDelivery arrowPoisonBehavior = target.GetBehavior<EntityBehaviorArrowPoisonDelivery>();
            if (arrowPoisonBehavior == null) return;

            if (cluster == "TOXICPOISON")
            {
                ApplyToxicArrowHit(target, null);
                return;
            }

            if (cluster == "NEUROTOXICPOISON" && new Random().NextDouble() < NeurotoxicArrowHitChance)
            {
                arrowPoisonBehavior.AddNeurotoxicStack();
            }
        }

        private static void ApplyToxicArrowHit(Entity target, EntityBehaviorRemedyEffects remedyBehavior)
        {
            if (remedyBehavior?.IsPostAntidoteWindowActive() ?? false) return; // poison immunity active

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
                effectMultiplier = effectMultiplier,
                onsetMultiplier = ArrowHitOnsetMultiplier
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
                effectMultiplier = effectMultiplier,
                onsetMultiplier = ArrowHitOnsetMultiplier
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }

        // A missed roll applies nothing at all. A successful roll contributes half a stage's
        // worth of ladder progress (EntityBehaviorRemedyEffects.ApplyNeurotoxicLadder treats this
        // as 0.5 toward the next dose-number threshold, at half of Weakness's own 6h baseline
        // duration) rather than a full dose the way Noxious/Cardiac's own arrow hits do - an
        // arrow-hit can never directly trigger Paralysis's second-instance stacking or Cardiac
        // Arrest's package on its own, only a drunk dose can.
        private static void ApplyNeurotoxicArrowHit(EntityBehaviorRemedyEffects remedyBehavior, ItemStack arrowStack)
        {
            if (new Random().NextDouble() >= NeurotoxicArrowHitChance) return;

            float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
            var effect = new EntityBehaviorRemedyEffects.EffectStruct(EntityBehaviorRemedyEffects.EffectCluster.NEUROTOXICPOISON)
            {
                isPoison = true,
                effectMultiplier = effectMultiplier,
                onsetMultiplier = ArrowHitOnsetMultiplier,
                ladderWeight = 0.5f
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }

        private static void ApplyMindPoisonArrowHit(EntityBehaviorRemedyEffects remedyBehavior, ItemStack arrowStack)
        {
            if (new Random().NextDouble() >= MindPoisonArrowHitChance) return;

            float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
            var effect = new EntityBehaviorRemedyEffects.EffectStruct(EntityBehaviorRemedyEffects.EffectCluster.MINDPOISON)
            {
                isPoison = true,
                effectMultiplier = effectMultiplier,
                onsetMultiplier = ArrowHitOnsetMultiplier
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }
    }

    /// <summary>
    /// Hunting-risk consequence of Toxic Poison's arrow-delivered secondary DoT
    /// (Patch_ArrowPoisonDelivery.ApplyToxicArrowHit): an animal it lands the killing blow on
    /// drops no meat, but keeps every other drop (hide, fat, antlers, bones). Marks the carcass at
    /// death; Patch_ArrowToxicDoTMeatSuppression strips the meat-coded drops after GenerateDrops
    /// runs normally. EntityBehaviorHealth.ProcessDoTEffects rebuilds a bare DamageSource per tick
    /// carrying only Source/Type/DamageTier, so DamageTier is the only field that survives into
    /// Die's damageSourceForDeath and is this DoT's sole identifying mark at the point of death.
    /// </summary>
    [HarmonyPatch(typeof(Entity), "Die")]
    public static class Patch_ArrowToxicDoTHuntingRisk
    {
        internal const string SuppressMeatDropsAttributeKey = "remedyandruinSuppressMeatDrops";

        public static void Postfix(Entity __instance, EnumDespawnReason reason, DamageSource damageSourceForDeath)
        {
            if (reason != EnumDespawnReason.Death || damageSourceForDeath == null) return;
            if (__instance is EntityPlayer) return;
            if (damageSourceForDeath.Source != EnumDamageSource.Internal) return;
            if (damageSourceForDeath.Type != EnumDamageType.Poison) return;
            if (damageSourceForDeath.DamageTier != EffectThreadManager.ArrowBonusToxicDoTDamageTier) return;

            __instance.WatchedAttributes.SetBool(SuppressMeatDropsAttributeKey, true);
        }
    }

    /// <summary>
    /// Strips meat-coded drops (redmeat/bushmeat/poultry, any variant - the game's only three meat
    /// item bases) from a carcass's harvest inventory right after GenerateDrops populates it, but
    /// only when Patch_ArrowToxicDoTHuntingRisk marked the carcass at death. Every other drop
    /// (hide, fat, antlers, bones) is left untouched.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHarvestable), "GenerateDrops")]
    public static class Patch_ArrowToxicDoTMeatSuppression
    {
        private static readonly string[] MeatCodePrefixes = { "redmeat-", "bushmeat-", "poultry-" };

        public static void Postfix(EntityBehaviorHarvestable __instance)
        {
            Entity harvestedEntity = __instance.entity;

            // A Harmony postfix runs after GenerateDrops returns no matter which internal path it
            // took - including the client's own immediate no-op (GenerateDrops's very first line
            // is "if (Side == Client || DropsGenerated) return;"). This behavior is instantiated
            // on both sides for this entity, so without this guard, every one of the calls below
            // was also running client-side, writing "harvestableInv" from a context vanilla itself
            // never lets GenerateDrops touch.
            if (harvestedEntity.World.Side != EnumAppSide.Server) return;

            if (!harvestedEntity.WatchedAttributes.GetBool(Patch_ArrowToxicDoTHuntingRisk.SuppressMeatDropsAttributeKey)) return;

            bool anyRemoved = false;
            foreach (ItemSlot slot in __instance.Inventory)
            {
                string path = slot.Itemstack?.Collectible?.Code?.Path;
                if (path == null) continue;

                foreach (string prefix in MeatCodePrefixes)
                {
                    if (path.StartsWith(prefix))
                    {
                        slot.Itemstack = null;
                        anyRemoved = true;
                        break;
                    }
                }
            }

            if (!anyRemoved) return;

            // GenerateDrops already serialized and synced the pre-filter inventory to
            // "harvestableInv"/marked it dirty before this postfix ran - that snapshot has to be
            // redone here, or the client keeps showing (and can still take) the removed meat. Must
            // match GenerateDrops' own sync sequence exactly (same four calls, same order) -
            // "harvested" needs re-marking dirty too, and the chunk needs to be marked modified on
            // the server, or the entity's stored state and the chunk's persisted state disagree
            // about what's actually in the inventory.
            TreeAttribute tree = new TreeAttribute();
            __instance.Inventory.ToTreeAttributes(tree);
            harvestedEntity.WatchedAttributes["harvestableInv"] = tree;
            harvestedEntity.WatchedAttributes.MarkPathDirty("harvestableInv");
            harvestedEntity.WatchedAttributes.MarkPathDirty("harvested");
            if (harvestedEntity.World.Side == EnumAppSide.Server)
            {
                harvestedEntity.World.BlockAccessor.GetChunkAtBlockPos(harvestedEntity.Pos.AsBlockPos).MarkModified();
            }
        }
    }
}
