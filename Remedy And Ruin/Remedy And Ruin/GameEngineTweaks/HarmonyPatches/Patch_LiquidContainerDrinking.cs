using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// BlockLiquidContainerBase.tryEatStop is a complete override of CollectibleObject.tryEatStop
    /// (the method Patch_RawEating patches) - drinking from a Vial, Crock, Jug, or any other
    /// liquid container never reaches the base method at all, so this mod's effect pipeline was
    /// never triggered by drinking. Captures the container's content stack before the base call
    /// and compares it against the content stack afterward; a decreased stack size means a genuine
    /// drink happened, without needing to duplicate BlockLiquidContainerBase's own internal gating
    /// (minimum use duration, server-side check, etc.).
    /// </summary>
    [HarmonyPatch(typeof(BlockLiquidContainerBase), "tryEatStop")]
    public static class Patch_LiquidContainerDrinking
    {
        public static void Prefix(BlockLiquidContainerBase __instance, ItemSlot slot, out (ItemStack contentBefore, int chargesBefore) __state)
        {
            int chargesBefore = (__instance is BlockVial && slot?.Itemstack != null)
                ? slot.Itemstack.Attributes.GetInt("poisonCharges", 9)
                : 9;
            __state = (__instance.GetContent(slot?.Itemstack)?.Clone(), chargesBefore);
        }

        public static void Postfix(BlockLiquidContainerBase __instance, ItemSlot slot, EntityAgent byEntity, (ItemStack contentBefore, int chargesBefore) __state)
        {
            ItemStack contentBefore = __state.contentBefore;
            if (contentBefore == null)
            {
                return;
            }

            ItemStack contentAfter = __instance.GetContent(slot?.Itemstack);
            int sizeAfter = contentAfter?.StackSize ?? 0;
            if (sizeAfter >= contentBefore.StackSize)
            {
                return; // nothing was actually drunk
            }

            float potencyScale = 1.0f;
            if (__instance is BlockVial && slot?.Itemstack != null)
            {
                // Drinking always fully drains the Vial's content, so the original tryEatStop call
                // above already emptied it via SetContent(null), which Patch_VialChargeNormalization
                // has already cleared poisonCharges for - no need to touch the attribute here.
                potencyScale = __state.chargesBefore / 9f;
            }

            EntityBehaviorRemedyEffects remedyBehavior = byEntity.GetBehavior<EntityBehaviorRemedyEffects>();
            remedyBehavior?.OnAnyItemConsumed(contentBefore, byEntity.World);

            JsonObject effectData = contentBefore.Collectible?.Attributes?["remedyandruinEffect"];
            if (effectData == null || !effectData.Exists)
            {
                effectData = EffectByTypeResolver.Resolve(contentBefore.Collectible);
            }
            if (effectData == null)
            {
                return;
            }

            remedyBehavior?.OnItemConsumed(contentBefore, effectData, potencyScale);
        }
    }
}
