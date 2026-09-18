using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Attached to every non-player entity so a poisoned arrow has something to apply its effect
    /// to. Deliberately lightweight compared to EntityBehaviorRemedyEffects (which players use) -
    /// no onset timers, no persistence beyond a raw stack count, no tolerance tracking. Non-player
    /// targets only ever receive Liverbane's instant chance-DoT (handled directly in
    /// Patch_ArrowPoisonDelivery against the target's EntityBehaviorHealth - no state needed here)
    /// and Neurotoxic's paralysis stacks (tracked here). Every other cluster no-ops against
    /// non-player targets.
    ///
    /// GetImmunityLevel: constructs (bells, locusts, the Eidolon) carry vanilla's own "mechanical"
    /// tag and are immune to every poison cluster. Drifter/Shiver/Bowtorn carry "rust-creature" and
    /// are immune to everything except Neurotoxic Poison, which works on them normally.
    ///
    /// AddNeurotoxicStack: each successful Neurotoxic arrow proc adds one stack (max 3), each worth
    /// a flat 25% walkspeed reduction that never decays on its own. Patch_MobNeurotoxicWalkSpeed is
    /// what actually makes the "walkspeed" stat affect movement here - the base
    /// EntityAgent.GetWalkSpeedMultiplier() vanilla uses for everything except EntityPlayer and
    /// EntityLocust never reads that stat on its own.
    /// </summary>
    public class EntityBehaviorArrowPoisonDelivery : EntityBehavior
    {
        public enum ImmunityLevel
        {
            Normal,
            NeurotoxicOnly,
            FullyImmune
        }

        public const string NeurotoxicStacksAttributeKey = "remedyandruinNeurotoxicStacks";
        private const string NeurotoxicWalkSpeedModifierKey = "remedyandruinNeurotoxicParalysis";
        private const int MaxNeurotoxicStacks = 3;
        private const float NeurotoxicStackSpeedPenalty = 0.25f;

        private static TagSetFast? mechanicalTags;
        private static TagSetFast? rustCreatureTags;

        public EntityBehaviorArrowPoisonDelivery(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinArrowPoisonDelivery";

        public static ImmunityLevel GetImmunityLevel(Entity target)
        {
            EnsureTagSetsResolved(target);
            if (mechanicalTags.HasValue && target.Tags.Overlaps(mechanicalTags.Value))
            {
                return ImmunityLevel.FullyImmune;
            }
            if (rustCreatureTags.HasValue && target.Tags.Overlaps(rustCreatureTags.Value))
            {
                return ImmunityLevel.NeurotoxicOnly;
            }
            return ImmunityLevel.Normal;
        }

        private static void EnsureTagSetsResolved(Entity target)
        {
            if (mechanicalTags.HasValue && rustCreatureTags.HasValue) return;

            var registry = target.Api?.EntityTagRegistry;
            if (registry == null) return;

            if (!mechanicalTags.HasValue && registry.TryCreateTagSet(out TagSetFast mechanical, "mechanical") == TagRegistryError.None)
            {
                mechanicalTags = mechanical;
            }
            if (!rustCreatureTags.HasValue && registry.TryCreateTagSet(out TagSetFast rustCreature, "rust-creature") == TagRegistryError.None)
            {
                rustCreatureTags = rustCreature;
            }
        }

        public void AddNeurotoxicStack()
        {
            int stacks = entity.WatchedAttributes.GetInt(NeurotoxicStacksAttributeKey, 0);
            if (stacks >= MaxNeurotoxicStacks) return;

            stacks++;
            entity.WatchedAttributes.SetInt(NeurotoxicStacksAttributeKey, stacks);
            entity.Stats.Set("walkspeed", NeurotoxicWalkSpeedModifierKey, -(NeurotoxicStackSpeedPenalty * stacks), persistent: true);
        }
    }
}
