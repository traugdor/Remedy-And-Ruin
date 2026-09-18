using HarmonyLib;
using Vintagestory.API.Common;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// EntityAgent.GetWalkSpeedMultiplier never factors in Stats.GetBlended("walkspeed") - only
    /// EntityPlayer and EntityLocust override the method themselves to blend it in. Every other
    /// creature ignores that stat entirely, so patching the base (virtual) method here is what
    /// makes EntityBehaviorArrowPoisonDelivery's Neurotoxic walkspeed stat actually slow down
    /// non-player, non-locust targets. Harmony only intercepts calls that resolve to this exact
    /// base implementation, so EntityPlayer/EntityLocust (which have their own override) are
    /// unaffected and don't get the blend applied twice.
    /// </summary>
    [HarmonyPatch(typeof(EntityAgent), "GetWalkSpeedMultiplier")]
    public static class Patch_MobNeurotoxicWalkSpeed
    {
        public static void Postfix(EntityAgent __instance, ref double __result)
        {
            __result *= __instance.Stats.GetBlended("walkspeed");
        }
    }
}
