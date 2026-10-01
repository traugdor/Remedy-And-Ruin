using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Bleeding's and Wound Infection's per-tick damage would otherwise make the entity grunt its
    /// hurt sound once per tick for the whole condition's duration. Suppresses only that repeated
    /// grunt - identified by the tick's own DamageSource (EnumDamageSource.Bleed for Bleeding;
    /// EnumDamageSource.Internal + EnumDamageType.Injury, a combination nothing else in this mod or
    /// vanilla uses, for Wound Infection) - so the original bite that caused a bleed still plays its
    /// own sound (it carries the attacker's own damage source, not this tick marker), and any other
    /// cause of health loss (falling, drowning, starvation, a different hit) is untouched.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), "OnEntityReceiveDamage")]
    public static class Patch_SuppressConditionTickSoundWindow
    {
        internal static bool InSuppressedTick;

        public static void Prefix(DamageSource damageSource)
        {
            InSuppressedTick = damageSource.Source == EnumDamageSource.Bleed
                || (damageSource.Source == EnumDamageSource.Internal && damageSource.Type == EnumDamageType.Injury);
        }

        public static void Postfix()
        {
            InSuppressedTick = false;
        }
    }

    [HarmonyPatch(typeof(Entity), "PlayEntitySound")]
    public static class Patch_SuppressConditionTickSound
    {
        public static bool Prefix(string type)
        {
            return !ShouldSuppress(type);
        }

        // Only "hurt"/"smallhurt" are the per-damage-tick grunt this patch targets - "death" (and
        // anything else) plays normally even when a fatal tick lands while InSuppressedTick is still
        // true, since Die() runs synchronously inside the same OnEntityReceiveDamage call that
        // triggered it, before Patch_SuppressConditionTickSoundWindow's Postfix clears the flag.
        internal static bool ShouldSuppress(string type)
        {
            return Patch_SuppressConditionTickSoundWindow.InSuppressedTick && (type == "hurt" || type == "smallhurt");
        }
    }

    /// <summary>
    /// EntityPlayer overrides PlayEntitySound and never calls the base implementation for "hurt"/
    /// "smallhurt"/"death" - it routes straight to talkUtil.Talk(...) instead, so patching the base
    /// Entity method alone never intercepts a player's own hurt grunt.
    /// </summary>
    [HarmonyPatch(typeof(EntityPlayer), "PlayEntitySound")]
    public static class Patch_SuppressConditionTickSoundPlayer
    {
        public static bool Prefix(string type)
        {
            return !Patch_SuppressConditionTickSound.ShouldSuppress(type);
        }
    }
}
