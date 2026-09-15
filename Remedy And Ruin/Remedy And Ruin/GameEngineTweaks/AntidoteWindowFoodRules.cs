using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// The Antidote's 2-hour restricted-diet window (02-design-overview.md:1478-1489) allows only
    /// fruit juice and vegetable-only soup - everything else (solid food, alcohol, any other
    /// drink) re-triggers vomiting. These two rules are shared between raw eating and meal eating,
    /// which each have different data available (a single ItemStack vs. a recipe + ingredient
    /// list), so each gets its own entry point.
    /// </summary>
    public static class AntidoteWindowFoodRules
    {
        private static readonly HashSet<string> SoupProteinIngredientCodes = new HashSet<string>
        {
            "redmeat-raw", "redmeat-cured", "poultry-raw", "poultry-cured",
            "egg-chicken-raw", "fish-raw", "fish-cured"
        };

        public static bool IsSafeRawItem(ItemStack stack, IWorldAccessor world)
        {
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null) return true; // nothing being eaten - no violation to flag

            FoodNutritionProperties nutrition = collectible.GetNutritionProperties(world, stack, null);
            if (nutrition != null && nutrition.Intoxication > 0f) return false; // alcohol is always unsafe

            if (collectible.Code?.Path?.StartsWith("juiceportion-") == true) return true; // the one safe raw drink

            return false; // everything else (solid food, water, any other drink) is unsafe
        }

        public static bool IsSafeMeal(IWorldAccessor world, ItemStack containerStack, ItemStack[] contentStacks, BlockMeal block)
        {
            if (contentStacks == null) return true; // nothing being eaten - no violation to flag

            string recipeCode = block?.GetRecipeCode(world, containerStack);
            if (recipeCode != "soup") return false; // only the vanilla "soup" recipe can ever be safe

            foreach (ItemStack contentStack in contentStacks)
            {
                string code = contentStack?.Collectible?.Code?.Path;
                if (code != null && SoupProteinIngredientCodes.Contains(code)) return false; // has meat/egg/fish
            }
            return true; // a soup with no protein ingredient
        }
    }
}
