using HarmonyLib;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Replaces the metal-ingot texture that BlockEntityToolMold.UpdateRenderer assigns to the fill
    /// surface with the glass texture when the mold is a Vial Mold. UpdateRenderer is not virtual
    /// and is called from pouring, loading and initialization, so a postfix is the only hook that
    /// covers every path. Only the client has a renderer; on the server this does nothing.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(BlockEntityToolMold), nameof(BlockEntityToolMold.UpdateRenderer))]
    public static class Patch_VialMoldGlassTexture
    {
        public static void Postfix(BlockEntityToolMold __instance)
        {
            (__instance as BlockEntityVialMold)?.ApplyGlassTexture();
        }
    }
}
