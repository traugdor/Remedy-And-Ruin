using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Remedy_And_Ruin.RRBehaviors;

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
        public static void Prefix(BlockMeal __instance, IWorldAccessor world, IPlayer eatingPlayer, ItemSlot inSlot, ItemStack[] contentStacks, float remainingServings, bool mulwithStackSize)
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

            behavior.OnMealConsumed(world, inSlot?.Itemstack, contentStacks, __instance);

            // Once per eating action, however many servings or discolored ingredients it holds.
            if (world.Side == EnumAppSide.Server && contentStacks.Any(DiscoloredMeat.IsDiscolored))
            {
                behavior.OnDiscoloredMeatEaten();
            }

            if (world.Side == EnumAppSide.Server && inSlot?.Itemstack != null)
            {
                EntityBehaviorHunger hunger = eatingPlayer.Entity.GetBehavior<EntityBehaviorHunger>();
                // Vanilla's Consume eats nothing when the player has no room left to fill.
                if (hunger != null && hunger.MaxSaturation - hunger.Saturation > 0f)
                {
                    eatingPlayer.Entity.GetBehavior<EntityBehaviorUpsetStomach>()?.OnFoodConsumed(WorstIngredientSpoilage(world, inSlot, contentStacks), isAlcohol: false);
                }
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

        /// <summary>
        /// The highest Perish level among the meal's ingredients, read the way BlockMeal.Consume
        /// reads each ingredient for its own spoilage penalty. A meal whose time is frozen counts
        /// as fresh. One badly spoiled ingredient spoils the whole meal.
        /// </summary>
        private static float WorstIngredientSpoilage(IWorldAccessor world, ItemSlot mealSlot, ItemStack[] contentStacks)
        {
            if (mealSlot.Itemstack.Attributes.GetBool("timeFrozen")) return 0f;

            float worst = 0f;
            foreach (ItemStack contentStack in contentStacks)
            {
                if (contentStack == null) continue;

                float level = contentStack.Collectible.UpdateAndGetTransitionState(world, new DummySlot(contentStack, mealSlot.Inventory), EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
                if (level > worst) worst = level;
            }
            return worst;
        }
    }
}
