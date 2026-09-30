using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Bleeding eligibility is tag-driven rather than a species list, so it generically covers every
    /// current and future creature in each category:
    /// - "ferocious" (wolf, bear, hyena - genuinely aggressive predators) is always eligible.
    /// - "animal" without "ferocious" (chicken, pig, raccoon, deer, fox, etc.) is also eligible on
    ///   the same "landed a real hit" basis - these creatures' only player-targeting melee task is
    ///   gated by whenInEmotionState: "aggressiveondamage", so a hit from one only ever happens
    ///   because it was struck first. Fox is tagged "predator" but not "ferocious" and its
    ///   attack-the-player task is still aggressiveondamage-gated, so it correctly falls in this
    ///   bucket rather than the ferocious one.
    /// - "rust-creature" (Drifter/Shiver/Bowtorn) is kept on its own tier-gated rule instead of the
    ///   tag check, since tier-gating isn't expressible as a simple tag lookup: Drifter bleeds only
    ///   at corrupt/nightmare/double-headed tiers, Shiver bleeds at every tier, Bowtorn's melee never
    ///   bleeds.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), "OnEntityReceiveDamage")]
    public static class Patch_BleedingDelivery
    {
        private static readonly string[] DrifterBleedingTiers = { "drifter-corrupt", "drifter-nightmare", "drifter-double-headed" };

        private static TagSetFast? ferociousTag;
        private static TagSetFast? animalTag;
        private static TagSetFast? rustCreatureTag;

        public static void Prefix(EntityBehaviorHealth __instance, DamageSource damageSource, ref float damage)
        {
            if (__instance.entity.World.Side != EnumAppSide.Server) return;
            if (damageSource.Type == EnumDamageType.Heal) return;

            Entity attacker = damageSource.GetCauseEntity();
            if (attacker == null || !IsBleedingEligible(attacker)) return;

            EntityBehaviorPlayerConditions.BleedingTier? tier = EntityBehaviorPlayerConditions.DetermineBleedingTier(damage);
            if (!tier.HasValue) return;

            EntityBehaviorPlayerConditions conditions = __instance.entity.GetBehavior<EntityBehaviorPlayerConditions>();
            if (conditions == null) return;

            damage = conditions.ApplyBleeding(tier.Value, damage, damageSource);
        }

        private static bool IsBleedingEligible(Entity attacker)
        {
            EnsureTagsResolved(attacker);

            if (rustCreatureTag.HasValue && attacker.Tags.Overlaps(rustCreatureTag.Value))
            {
                string path = attacker.Code?.Path ?? "";
                foreach (string drifterTier in DrifterBleedingTiers)
                {
                    if (path.StartsWith(drifterTier)) return true;
                }
                return path.StartsWith("shiver-");
            }

            if (ferociousTag.HasValue && attacker.Tags.Overlaps(ferociousTag.Value)) return true;
            if (animalTag.HasValue && attacker.Tags.Overlaps(animalTag.Value)) return true;
            return false;
        }

        private static void EnsureTagsResolved(Entity attacker)
        {
            if (ferociousTag.HasValue && animalTag.HasValue && rustCreatureTag.HasValue) return;

            var registry = attacker.Api?.EntityTagRegistry;
            if (registry == null) return;

            if (!ferociousTag.HasValue && registry.TryCreateTagSet(out TagSetFast fer, "ferocious") == TagRegistryError.None) ferociousTag = fer;
            if (!animalTag.HasValue && registry.TryCreateTagSet(out TagSetFast ani, "animal") == TagRegistryError.None) animalTag = ani;
            if (!rustCreatureTag.HasValue && registry.TryCreateTagSet(out TagSetFast rust, "rust-creature") == TagRegistryError.None) rustCreatureTag = rust;
        }
    }
}
