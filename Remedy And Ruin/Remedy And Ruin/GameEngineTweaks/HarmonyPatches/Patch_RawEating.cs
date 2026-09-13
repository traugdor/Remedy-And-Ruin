using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Prefixes CollectibleObject.tryEatStop to identify the consumed item and hand it to
    /// EntityBehaviorRemedyEffects.OnItemConsumed; applies no effect itself. Must be a prefix -
    /// the vanilla method calls slot.TakeOut(1) partway through its own body, so slot.Itemstack
    /// may be empty or changed by the time a postfix runs.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
    public static class Patch_RawEating
    {
        public static void Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            ItemStack stack = slot?.Itemstack;
            JsonObject effectData = stack?.Collectible?.Attributes?["remedyandruinEffect"];
            if (effectData == null || !effectData.Exists)
            {
                // TODO: also check remedyandruinEffectByType (raw poison mushrooms, wildcard-keyed
                // by the specific variant code) once a helper exists to resolve it against stack.Collectible.Code.
                return;
            }

            byEntity.GetBehavior<EntityBehaviorRemedyEffects>()?.OnItemConsumed(stack, effectData);
        }
    }
}
