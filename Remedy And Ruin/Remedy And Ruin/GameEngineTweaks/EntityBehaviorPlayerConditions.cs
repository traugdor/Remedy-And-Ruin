using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Owns Bleeding, Wound Infection, and Skin Irritation state - the combat/environment-triggered
    /// physical conditions from 02-design-overview.md's Part 3, distinct from
    /// EntityBehaviorRemedyEffects' eaten/drunk poison-cluster pipeline. Attached to every entity
    /// with EntityBehaviorHealth on spawn (see Remedy And RuinModSystem.StartServerSide) since
    /// wildlife can bleed too, even though only players currently have any way to treat these
    /// conditions.
    /// </summary>
    public class EntityBehaviorPlayerConditions : EntityBehavior
    {
        // -5% healingeffectivness (02-design-overview.md:1188-1190) - the same stat category Wound
        // Infection and the Chest Cold family's later stages use, at the mildest magnitude in the
        // catalog.
        private const float SkinIrritationHealingEffectivnessPenalty = -0.05f;
        private const string SkinIrritationStatKey = "remedyandruinSkinIrritation";

        // Self-resolves in 2-3 in-game days if untreated (02-design-overview.md:1191) - midpoint of
        // that range.
        private const double SkinIrritationSelfResolveHours = 2.5 * 24.0;

        private long skinIrritationSelfResolveListenerId;

        public EntityBehaviorPlayerConditions(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinPlayerConditions";

        public bool HasSkinIrritation => entity.WatchedAttributes.GetBool("remedyandruinSkinIrritation", false);

        /// <summary>
        /// Applies Skin Irritation's -5% healingeffectivness debuff and starts its self-resolve
        /// timer. Re-applying while already active just restarts the timer - it doesn't stack.
        /// Trigger detection (frostbite/sunburn/irritant-plant contact) is not implemented here;
        /// call this from wherever that later gets built.
        /// </summary>
        public void ApplySkinIrritation(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            target.WatchedAttributes.SetBool("remedyandruinSkinIrritation", true);
            target.Stats.Set("healingeffectivness", SkinIrritationStatKey, SkinIrritationHealingEffectivnessPenalty, persistent: true);

            EntityBehaviorPlayerConditions conditions = target.GetBehavior<EntityBehaviorPlayerConditions>();
            if (conditions == null) return;

            if (conditions.skinIrritationSelfResolveListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(conditions.skinIrritationSelfResolveListenerId);
            }
            conditions.skinIrritationSelfResolveListenerId = target.World.RegisterCallback(
                dt => conditions.ClearSkinIrritation(target),
                (int)(SkinIrritationSelfResolveHours * CalendarTimeHelper.RealSecondsPerGameHour(target.World.Calendar) * 1000.0));
        }

        /// <summary>
        /// Clears Skin Irritation instantly, regardless of tier - applying a treated-bandage
        /// (Topical Ointment, any potency) does this per 02-design-overview.md:1192-1194, which
        /// explicitly makes no Concentrated-vs-regular distinction for this condition.
        /// </summary>
        public void ClearSkinIrritation(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;
            if (!target.WatchedAttributes.GetBool("remedyandruinSkinIrritation", false)) return;

            target.WatchedAttributes.SetBool("remedyandruinSkinIrritation", false);
            target.Stats.Remove("healingeffectivness", SkinIrritationStatKey);

            EntityBehaviorPlayerConditions conditions = target.GetBehavior<EntityBehaviorPlayerConditions>();
            if (conditions != null && conditions.skinIrritationSelfResolveListenerId != 0L)
            {
                target.World.UnregisterGameTickListener(conditions.skinIrritationSelfResolveListenerId);
                conditions.skinIrritationSelfResolveListenerId = 0L;
            }
        }
    }
}
