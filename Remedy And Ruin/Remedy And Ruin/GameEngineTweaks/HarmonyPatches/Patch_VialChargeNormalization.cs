using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Dip, craft, and drink all clear a Vial's poisonCharges attribute themselves when they empty
    /// it. Every other way a Vial's content can become empty - pouring into another container,
    /// spilling - goes through vanilla's own BlockLiquidContainerBase.SetContent without touching
    /// poisonCharges at all, leaving it stale. That breaks stacking (two empty Vials with different
    /// stale charge counts won't merge) and the refill gate (a poured-out Vial would still read as
    /// having charges left). This patches the single method every content-removal path funnels
    /// through, so an emptied Vial's charge count is always cleared to match.
    /// </summary>
    [HarmonyPatch(typeof(BlockLiquidContainerBase), "SetContent", new Type[] { typeof(ItemStack), typeof(ItemStack) })]
    public static class Patch_VialChargeNormalization
    {
        public static void Postfix(BlockLiquidContainerBase __instance, ItemStack containerStack, ItemStack content)
        {
            if (content == null && __instance is BlockVial)
            {
                containerStack.Attributes.RemoveAttribute("poisonCharges");
            }
        }
    }
}
