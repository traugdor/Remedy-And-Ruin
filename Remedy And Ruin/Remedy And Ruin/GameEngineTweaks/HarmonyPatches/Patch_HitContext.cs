using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Classifies each server-side non-Heal hit on a player exactly once, before vanilla armor runs, and
    /// publishes it as <see cref="HitContext.Current"/>. The finalizer clears it after the whole method,
    /// including the PostArmorDamagePipeline postfix, so post-armor handlers can still read it.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), "ApplyOnDamageDelegates")]
    public static class Patch_HitContext
    {
        public static void Prefix(EntityBehaviorHealth __instance, DamageSource damageSource)
        {
            HitContext.Current = null;
            if (__instance.entity.World.Side != EnumAppSide.Server) return;
            if (__instance.entity is not EntityPlayer victim) return;
            if (damageSource.Type == EnumDamageType.Heal) return;

            BoneLocation? forced = HitContext.ForcedLocation;
            HitContext.Current = forced.HasValue
                ? new HitClassification(forced.Value, true)
                : HitLocationClassifier.Classify(victim, damageSource);
        }

        public static void Finalizer()
        {
            HitContext.Current = null;
        }
    }
}
