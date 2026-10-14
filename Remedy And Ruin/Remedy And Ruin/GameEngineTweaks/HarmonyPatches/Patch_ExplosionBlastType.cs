using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Exposes the blast type of the explosion currently being resolved. ServerMain.CreateExplosion
    /// damages entities synchronously inside the call with a DamageSource that carries neither a
    /// source entity nor the blast type, so damage handlers read <see cref="Current"/> to tell
    /// explosion kinds apart. Only valid on the thread running CreateExplosion; null otherwise.
    /// </summary>
    [HarmonyPatch(typeof(ServerMain), nameof(ServerMain.CreateExplosion))]
    public static class Patch_ExplosionBlastType
    {
        [ThreadStatic]
        private static EnumBlastType? current;

        public static EnumBlastType? Current
        {
            get => current;
            internal set => current = value;
        }

        public static void Prefix(EnumBlastType blastType)
        {
            current = blastType;
        }

        // Finalizer runs on exceptions too, so the value can't outlive the call.
        public static Exception Finalizer(Exception __exception)
        {
            current = null;
            return __exception;
        }
    }
}
