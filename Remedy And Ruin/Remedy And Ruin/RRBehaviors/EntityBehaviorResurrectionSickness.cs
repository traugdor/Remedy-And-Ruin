using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.RRBehaviors
{
    /// <summary>
    /// Owns Resurrection Sickness: on every player revival, Temporal Stability is set to exactly
    /// 50% and the sickness lasts one in-game day, during which positive Temporal Stability regen
    /// is reduced to RegenMultiplier of its normal rate (see
    /// Patch_ResurrectionSicknessRegenDampening). Drain is unaffected. There is no cure.
    ///
    /// The sickness is stored as an end day on the calendar and evaluated lazily (IsActive), so it
    /// follows calendar speed, survives saves and relogs, and needs no timers. IsActive is static
    /// and reads only WatchedAttributes so it is valid on the client, where this behavior instance
    /// may not exist.
    /// </summary>
    public class EntityBehaviorResurrectionSickness : EntityBehavior
    {
        public const float RegenMultiplier = 0.25f;
        private const double ResurrectionStability = 0.5;
        private const double DurationInGameDays = 1.0;
        private const string EndDayKey = "remedyandruinResurrectionSicknessEndDay";

        public EntityBehaviorResurrectionSickness(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinResurrectionSickness";

        public static bool IsActive(Entity entity)
        {
            if (entity?.WatchedAttributes == null || entity.World?.Calendar == null) return false;

            return entity.World.Calendar.TotalDays < entity.WatchedAttributes.GetDouble(EndDayKey, 0.0);
        }

        public bool HasResurrectionSickness => IsActive(entity);

        /// <summary>
        /// Starts (or restarts) the one-day sickness. Server only; does not touch Temporal Stability.
        /// </summary>
        public void ApplyResurrectionSickness(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            SetEndDay(target, target.World.Calendar.TotalDays + DurationInGameDays);
        }

        public void ClearResurrectionSickness(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            SetEndDay(target, 0.0);
        }

        public override void OnEntityRevive()
        {
            if (entity.World.Side != EnumAppSide.Server) return;
            if (!(entity is EntityAgent agent)) return;

            ApplyResurrectionSickness(agent);

            EntityBehaviorTemporalStabilityAffected stability = entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
            if (stability != null)
            {
                stability.OwnStability = ResurrectionStability;
            }
        }

        private static void SetEndDay(Entity target, double endDay)
        {
            target.WatchedAttributes.SetDouble(EndDayKey, endDay);
            target.WatchedAttributes.MarkPathDirty(EndDayKey);
        }
    }
}
