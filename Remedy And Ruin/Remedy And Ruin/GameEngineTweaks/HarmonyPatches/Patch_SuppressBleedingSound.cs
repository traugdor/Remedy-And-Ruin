using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// A Bleeding DoT's per-tick damage (EntityBehaviorPlayerConditions.ApplyBleeding, tagged
    /// EnumDamageSource.Bleed) would otherwise make the entity grunt its hurt sound once per tick
    /// for the whole bleed duration. Suppresses only that repeated grunt - the original bite that
    /// started the bleed still carries the attacker's own damage source (Entity), so it keeps its
    /// sound, and any other cause of health loss (falling, drowning, a different hit) is untouched.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), "OnEntityReceiveDamage")]
    public static class Patch_SuppressBleedingSoundWindow
    {
        internal static bool InBleedingTick;

        public static void Prefix(DamageSource damageSource)
        {
            InBleedingTick = damageSource.Source == EnumDamageSource.Bleed;
        }

        public static void Postfix()
        {
            InBleedingTick = false;
        }
    }

    [HarmonyPatch(typeof(Entity), "PlayEntitySound")]
    public static class Patch_SuppressBleedingSound
    {
        public static bool Prefix(string type)
        {
            return !ShouldSuppress(type);
        }

        // Only "hurt"/"smallhurt" are the per-damage-tick grunt this patch targets - "death" (and
        // anything else) plays normally even when a fatal tick lands while InBleedingTick is still
        // true, since Die() runs synchronously inside the same OnEntityReceiveDamage call that
        // triggered it, before Patch_SuppressBleedingSoundWindow's Postfix clears the flag.
        internal static bool ShouldSuppress(string type)
        {
            return Patch_SuppressBleedingSoundWindow.InBleedingTick && (type == "hurt" || type == "smallhurt");
        }
    }

    /// <summary>
    /// EntityPlayer overrides PlayEntitySound and never calls the base implementation for "hurt"/
    /// "smallhurt"/"death" - it routes straight to talkUtil.Talk(...) instead, so patching the base
    /// Entity method alone never intercepts a player's own hurt grunt.
    /// </summary>
    [HarmonyPatch(typeof(EntityPlayer), "PlayEntitySound")]
    public static class Patch_SuppressBleedingSoundPlayer
    {
        public static bool Prefix(string type)
        {
            return !Patch_SuppressBleedingSound.ShouldSuppress(type);
        }
    }
}
