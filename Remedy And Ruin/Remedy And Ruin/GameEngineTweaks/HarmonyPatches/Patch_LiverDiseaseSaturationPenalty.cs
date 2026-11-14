using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Remedy_And_Ruin.RRBehaviors;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// EntityBehaviorHunger.OnEntityReceiveSaturation adds its saturation parameter directly, with
    /// no existing stat-multiplier hook (unlike hungerrate, which only affects saturation loss, not
    /// gain) - this Prefix is the only way to apply Liver Disease's -10% saturation-from-food
    /// penalty and the Topical Ointment's satiety cut (PotionEffects.TopicalSatietyMultiplier).
    /// The two multiply. The Topical Ointment cut only scales gains, never a negative value.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHunger), "OnEntityReceiveSaturation")]
    public static class Patch_LiverDiseaseSaturationPenalty
    {
        public static void Prefix(EntityBehaviorHunger __instance, ref float saturation)
        {
            if (__instance.entity.World.Side != EnumAppSide.Server) return;

            EntityBehaviorLiverDisease liverDiseaseBehavior = __instance.entity.GetBehavior<EntityBehaviorLiverDisease>();
            if (liverDiseaseBehavior != null)
            {
                saturation = liverDiseaseBehavior.ApplySaturationPenalty(saturation);
            }

            if (saturation > 0f)
            {
                saturation *= PotionEffects.TopicalSatietyMultiplier(__instance.entity);
            }
        }
    }
}
