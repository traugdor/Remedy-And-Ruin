using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Remedy_And_Ruin.RRBehaviors;

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

            EntityBehaviorRemedyEffects remedyBehavior = byEntity.GetBehavior<EntityBehaviorRemedyEffects>();
            remedyBehavior?.OnAnyItemConsumed(stack, byEntity.World);

            // tryEatStop runs on every release of an interact with any held item; mirror vanilla's
            // own "completed eat of an actually-edible item" gate so only real meals are counted.
            FoodNutritionProperties nutritionProps = secondsUsed >= 0.95f
                ? stack?.Collectible?.GetNutritionProperties(byEntity.World, stack, byEntity)
                : null;
            if (nutritionProps != null)
            {
                EntityBehaviorLiverDisease liverDiseaseBehavior = byEntity.GetBehavior<EntityBehaviorLiverDisease>();
                liverDiseaseBehavior?.OnFoodConsumed(stack, byEntity);

                if (byEntity.World.Side == EnumAppSide.Server && DiscoloredMeat.IsDiscolored(stack))
                {
                    remedyBehavior?.OnDiscoloredMeatEaten();
                }

                if (byEntity.World.Side == EnumAppSide.Server && EntityBehaviorUpsetStomach.IsRealFood(nutritionProps))
                {
                    // The same Perish lookup vanilla's tryEatStop uses for its spoilage penalties.
                    float spoilage = stack.Collectible.UpdateAndGetTransitionState(byEntity.World, slot, EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
                    byEntity.GetBehavior<EntityBehaviorUpsetStomach>()?.OnFoodConsumed(spoilage, nutritionProps.Intoxication > 0f);
                }
            }

            JsonObject effectData = stack?.Collectible?.Attributes?["remedyandruinEffect"];
            if (effectData == null || !effectData.Exists)
            {
                effectData = EffectByTypeResolver.Resolve(stack?.Collectible);
            }
            if (effectData == null)
            {
                return;
            }

            remedyBehavior?.OnItemConsumed(stack, effectData);
        }
    }
}
