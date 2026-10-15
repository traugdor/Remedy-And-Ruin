using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Marks a [HarmonyPatch] class as part of the shared patch set: patches that must run on a pure
    /// client and on a pure server, and that read the acting entity's world side internally instead of
    /// assuming one. Unmarked [HarmonyPatch] classes form the server set.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SharedPatchAttribute : Attribute
    {
    }

    /// <summary>
    /// Applies the mod's Harmony patches exactly once per process. In singleplayer the client and the
    /// integrated server share one process, so a patch applied from both entry points would stack.
    ///
    /// Server set: every [HarmonyPatch] class without <see cref="SharedPatchAttribute"/>. Applied from
    /// the server entry point only, by <see cref="EnsureServerSet"/>; these patches target
    /// server-authoritative logic.
    ///
    /// Shared set: every class marked <see cref="SharedPatchAttribute"/>. Applied by whichever entry
    /// point runs first, by <see cref="EnsureSharedSet"/>; it is present on a dedicated server, on a
    /// remote client and in singleplayer. Shared patches are called with the client's entity on the
    /// client and the server's entity on the server, so they must branch on the world side themselves.
    ///
    /// <see cref="Reset"/> removes all patches and clears both flags; call it on dispose.
    /// </summary>
    public static class PatchBootstrap
    {
        private const string HarmonyId = "remedyandruin";

        private static readonly object Gate = new object();
        private static Harmony harmony;
        private static bool serverSetApplied;
        private static bool sharedSetApplied;

        // Returns true when this call applied the server set, false when it was already applied.
        public static bool EnsureServerSet(ILogger logger)
        {
            lock (Gate)
            {
                if (serverSetApplied) return false;

                ApplySet(logger, shared: false);
                serverSetApplied = true;
                return true;
            }
        }

        public static void EnsureSharedSet(ILogger logger)
        {
            lock (Gate)
            {
                if (sharedSetApplied) return;

                ApplySet(logger, shared: true);
                sharedSetApplied = true;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                harmony?.UnpatchAll(HarmonyId);
                harmony = null;
                serverSetApplied = false;
                sharedSetApplied = false;
            }
        }

        // Each [HarmonyPatch] class is patched individually so one that cannot resolve its target
        // logs an error and is skipped instead of aborting the rest.
        private static void ApplySet(ILogger logger, bool shared)
        {
            harmony ??= new Harmony(HarmonyId);
            foreach (Type patchType in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (patchType.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0) continue;
                if ((patchType.GetCustomAttributes(typeof(SharedPatchAttribute), inherit: false).Length > 0) != shared) continue;

                try
                {
                    harmony.CreateClassProcessor(patchType).Patch();
                }
                catch (Exception ex)
                {
                    logger.Error($"remedyandruin: failed to apply Harmony patch {patchType.FullName} - {ex}");
                }
            }
        }
    }
}
