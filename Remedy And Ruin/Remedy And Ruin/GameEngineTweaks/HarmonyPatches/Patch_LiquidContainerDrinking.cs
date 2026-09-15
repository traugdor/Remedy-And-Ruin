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
        public static void Prefix(BlockLiquidContainerBase __instance, ItemSlot slot, out ItemStack __state)
        {
            __state = __instance.GetContent(slot?.Itemstack)?.Clone();
        }

        public static void Postfix(BlockLiquidContainerBase __instance, ItemSlot slot, EntityAgent byEntity, ItemStack __state)
        {
            if (__state == null)
            {
                return;
            }

            ItemStack contentAfter = __instance.GetContent(slot?.Itemstack);
            int sizeAfter = contentAfter?.StackSize ?? 0;
            if (sizeAfter >= __state.StackSize)
            {
                return; // nothing was actually drunk
            }

            float potencyScale = 1.0f;
            if (__instance is BlockVial && slot?.Itemstack != null)
            {
                int chargesBeforeDrink = slot.Itemstack.Attributes.GetInt("poisonCharges", 9);
                potencyScale = chargesBeforeDrink / 9f;
                slot.Itemstack.Attributes.SetInt("poisonCharges", 0); // drinking always fully depletes it
                slot.MarkDirty();
            }

            EntityBehaviorRemedyEffects remedyBehavior = byEntity.GetBehavior<EntityBehaviorRemedyEffects>();
            remedyBehavior?.OnAnyItemConsumed(__state, byEntity.World);

            JsonObject effectData = __state.Collectible?.Attributes?["remedyandruinEffect"];
            if (effectData == null || !effectData.Exists)
            {
                effectData = EffectByTypeResolver.Resolve(__state.Collectible);
            }
            if (effectData == null)
            {
                return;
            }

            remedyBehavior?.OnItemConsumed(__state, effectData, potencyScale);
        }
    }
}
