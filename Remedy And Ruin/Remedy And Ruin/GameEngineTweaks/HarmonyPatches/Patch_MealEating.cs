using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Prefixes BlockMeal.Consume to identify which meal ingredients carry a
    /// remedyandruinEffect/remedyandruinEffectByType attribute and hand each one to
    /// EntityBehaviorRemedyEffects.OnItemConsumed individually (design doc's "Stacking" model:
    /// each poison-cluster ingredient is its own effect instance, not summed). Applies no
    /// effect itself.
    /// </summary>
    [HarmonyPatch(typeof(BlockMeal), "Consume")]
    public static class Patch_MealEating
    {
        public static void Prefix(IWorldAccessor world, IPlayer eatingPlayer, ItemSlot inSlot, ItemStack[] contentStacks, float remainingServings, bool mulwithStackSize)
        {
            if (contentStacks == null || eatingPlayer?.Entity == null)
            {
                return;
            }

            EntityBehaviorRemedyEffects behavior = eatingPlayer.Entity.GetBehavior<EntityBehaviorRemedyEffects>();
            if (behavior == null)
            {
                return;
            }

            foreach (ItemStack stack in contentStacks)
            {
                JsonObject effectData = stack?.Collectible?.Attributes?["remedyandruinEffect"];
                if (effectData == null || !effectData.Exists)
                {
                    effectData = EffectByTypeResolver.Resolve(stack?.Collectible);
                }
                if (effectData == null)
                {
                    continue;
                }

                behavior.OnItemConsumed(stack, effectData);
            }
        }
    }
}
