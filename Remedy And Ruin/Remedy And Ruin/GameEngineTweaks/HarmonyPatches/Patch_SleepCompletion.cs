using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// DidUnmount fires on every bed dismount, natural or early - checking Tiredness at the moment
    /// it fires distinguishes them, since RestPlayer only ever calls TryUnmount() (which leads here)
    /// the instant Tiredness reaches 0, and never touches Tiredness on an early/manual dismount.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityBed), "DidUnmount")]
    public static class Patch_SleepCompletion
    {
        public static void Postfix(BlockEntityBed __instance, EntityAgent entityAgent)
        {
            if (entityAgent?.World?.Side != EnumAppSide.Server) return;

            float tiredness = entityAgent.GetBehavior<EntityBehaviorTiredness>()?.Tiredness ?? -1f;
            if (tiredness < 0f) return; // no tiredness behavior on this entity - not a real sleeper

            SleepCompletionEvents.RaiseSleepEnded(entityAgent, tiredness <= 0.0001f);
        }
    }
}
