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
    /// penalty.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHunger), "OnEntityReceiveSaturation")]
    public static class Patch_LiverDiseaseSaturationPenalty
    {
        public static void Prefix(EntityBehaviorHunger __instance, ref float saturation)
        {
            if (__instance.entity.World.Side != EnumAppSide.Server) return;

            EntityBehaviorLiverDisease liverDiseaseBehavior = __instance.entity.GetBehavior<EntityBehaviorLiverDisease>();
            if (liverDiseaseBehavior == null) return;

            saturation = liverDiseaseBehavior.ApplySaturationPenalty(saturation);
        }
    }
}
