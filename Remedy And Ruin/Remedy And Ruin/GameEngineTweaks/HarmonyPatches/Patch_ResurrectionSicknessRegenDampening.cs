using HarmonyLib;
using Vintagestory.GameContent;
using Remedy_And_Ruin.RRBehaviors;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// EntityBehaviorTemporalStabilityAffected.OnGameTick adds its regen/drain step to OwnStability
    /// in a single statement with no hook. The Prefix records OwnStability and the Postfix scales
    /// any positive step by RegenMultiplier while Resurrection Sickness is active; drain is left
    /// untouched. Runs on both sides because the tick does, and the sickness state is read from
    /// WatchedAttributes rather than a behavior instance.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorTemporalStabilityAffected), "OnGameTick")]
    public static class Patch_ResurrectionSicknessRegenDampening
    {
        public static void Prefix(EntityBehaviorTemporalStabilityAffected __instance, out double __state)
        {
            __state = __instance.OwnStability;
        }

        public static void Postfix(EntityBehaviorTemporalStabilityAffected __instance, double __state)
        {
            double delta = __instance.OwnStability - __state;
            if (delta <= 0) return;
            if (!EntityBehaviorResurrectionSickness.IsActive(__instance.entity)) return;

            __instance.OwnStability = __state + delta * EntityBehaviorResurrectionSickness.RegenMultiplier;
        }
    }
}
