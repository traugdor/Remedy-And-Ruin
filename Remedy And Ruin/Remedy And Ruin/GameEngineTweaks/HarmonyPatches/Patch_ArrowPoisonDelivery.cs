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
    /// </summary>
    [HarmonyPatch(typeof(EntityProjectileBase), "DealDamage")]
    public static class Patch_ArrowPoisonDelivery
    {
        public static void Postfix(EntityProjectileBase __instance, Entity target, bool __result)
        {
            if (!__result) return; // no actual hit landed

            ItemStack arrowStack = __instance.ProjectileStack;
            string cluster = arrowStack?.Attributes.GetString("remedyandruinArrowPoisonCluster");
            if (string.IsNullOrEmpty(cluster)) return;

            EntityBehaviorRemedyEffects remedyBehavior = target.GetBehavior<EntityBehaviorRemedyEffects>();
            if (remedyBehavior == null) return;

            float effectMultiplier = arrowStack.Attributes.GetFloat("remedyandruinArrowPoisonEffectMultiplier");
            EntityBehaviorRemedyEffects.EffectStruct effect = new EntityBehaviorRemedyEffects.EffectStruct(cluster.ToEnum<EntityBehaviorRemedyEffects.EffectCluster>())
            {
                isPoison = true,
                effectMultiplier = effectMultiplier
            };
            remedyBehavior.ApplyEffect(effect, forceIneligibleForTolerance: true);
        }
    }
}
