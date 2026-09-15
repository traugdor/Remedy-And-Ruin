using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// "Picking up a poisoned arrow strips it" (02-design-overview.md §8) - EntityProjectileBase
    /// .OnCollected returns the ItemStack handed to whoever retrieves a stuck arrow; this removes
    /// the poison tag from that returned stack, without touching the arrow entity's own
    /// ProjectileStack (so an arrow that missed and is still mid-flight or freshly stuck keeps its
    /// poison until someone actually picks it up).
    /// </summary>
    [HarmonyPatch(typeof(EntityProjectileBase), "OnCollected")]
    public static class Patch_ArrowPoisonPickupStrip
    {
        public static void Postfix(ref ItemStack __result)
        {
            if (__result == null) return;
            if (!__result.Attributes.HasAttribute("remedyandruinArrowPoisonCluster")) return;

            __result = __result.Clone();
            __result.Attributes.RemoveAttribute("remedyandruinArrowPoisonCluster");
            __result.Attributes.RemoveAttribute("remedyandruinArrowPoisonEffectMultiplier");
        }
    }
}
