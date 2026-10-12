using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.RRBehaviors
{
    /// <summary>
    /// Owns the Chest Cold family stage machine: None -> ChestCold -> Bronchitis / Flu -> Pneumonia.
    ///
    /// Debuffs (healingeffectivness / walkspeed): ChestCold none; Bronchitis -10% / 0; Flu -10% /
    /// -5%; Pneumonia -20% / -10%.
    ///
    /// Escalation is decided once per night window by the cold-and-wet exposure accumulated since
    /// the stage began or the previous morning check: freezingEffectStrength above 0 (written by
    /// EntityBehaviorBodyTemperature) and Wetness above 0.1, summed as in-game hours on a calendar
    /// delta. At the first MorningHour after the window opens, ChestCold becomes Flu at 4h or more,
    /// Bronchitis at 2h or more, and otherwise stays put; Bronchitis and Flu become Pneumonia at 1h
    /// or more. Each check closes the window and opens the next one. Pneumonia does not escalate
    /// further. The accumulator and the next check time are persisted.
    ///
    /// Self-resolve: ChestCold 3 in-game days, Bronchitis 7, Flu 3, Pneumonia never. The end day is
    /// stored on the calendar in WatchedAttributes and checked from the tick listener, so it follows
    /// calendar speed and sleep and survives relogs. The timer restarts at every stage change.
    ///
    /// The tick listener is not persisted; ResumeAfterLogin re-registers it and reconciles stat
    /// modifiers for the stored stage. Death clears the illness entirely.
    ///
    /// CurrentStage: the persisted stage.
    /// TriggerChestCold: starts ChestCold if healthy; no-op if already sick. Server only.
    /// CureCurrentStage: returns to None. ReverseOneStage: Pneumonia -> Bronchitis, Bronchitis or
    /// Flu -> ChestCold, ChestCold -> None. DebugSetStage: forces any stage.
    /// </summary>
    public class EntityBehaviorChestColdFamily : EntityBehavior
    {
        public enum Stage
        {
            None,
            ChestCold,
            Bronchitis,
            Flu,
            Pneumonia
        }

        private const string StageKey = "remedyandruinChestColdStage";
        private const string EndDayKey = "remedyandruinChestColdEndDay";
        private const string HealStatKey = "remedyandruinChestColdHeal";
        private const string WalkStatKey = "remedyandruinChestColdWalk";
        private const string FreezingKey = "freezingEffectStrength";

        private const string ExposureKey = "remedyandruinChestColdExposureHours";
        private const string NextCheckKey = "remedyandruinChestColdNextCheckHours";

        private const double WetnessThreshold = 0.1;
        private const double MorningHour = 6.0;
        private const double FluExposureHours = 4.0;
        private const double BronchitisExposureHours = 2.0;
        private const double PneumoniaExposureHours = 1.0;
        private const int TickIntervalMs = 5000;

        private long tickListenerId;
        private double lastExposureCheckHours;

        private double ExposureHours
        {
            get => entity.WatchedAttributes.GetDouble(ExposureKey, 0.0);
            set => entity.WatchedAttributes.SetDouble(ExposureKey, value);
        }

        public EntityBehaviorChestColdFamily(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinChestColdFamily";

        public Stage CurrentStage => (Stage)entity.WatchedAttributes.GetInt(StageKey, (int)Stage.None);

        public void TriggerChestCold(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;
            if (CurrentStage != Stage.None) return;

            SetStage(target, Stage.ChestCold);
        }

        public void CureCurrentStage(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            SetStage(target, Stage.None);
        }

        public void ReverseOneStage(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            switch (CurrentStage)
            {
                case Stage.Pneumonia:
                    SetStage(target, Stage.Bronchitis);
                    break;
                case Stage.Bronchitis:
                case Stage.Flu:
                    SetStage(target, Stage.ChestCold);
                    break;
                case Stage.ChestCold:
                    SetStage(target, Stage.None);
                    break;
            }
        }

        public void DebugSetStage(EntityAgent target, Stage stage)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            SetStage(target, stage);
        }

        public void ResumeAfterLogin(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            Stage stage = CurrentStage;
            if (stage == Stage.None)
            {
                RemoveDebuffs(target);
                StopTicking(target);
                return;
            }

            double endDay = target.WatchedAttributes.GetDouble(EndDayKey, 0.0);
            if (endDay > 0.0 && target.World.Calendar.TotalDays >= endDay)
            {
                SetStage(target, Stage.None);
                return;
            }

            ApplyStageDebuff(target, stage);
            StartTicking(target);
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side != EnumAppSide.Server || !(entity is EntityAgent agent)) return;

            SetStage(agent, Stage.None);
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (tickListenerId != 0L)
            {
                entity.World.UnregisterGameTickListener(tickListenerId);
                tickListenerId = 0L;
            }
        }

        private void SetStage(EntityAgent target, Stage stage)
        {
            target.WatchedAttributes.SetInt(StageKey, (int)stage);
            target.WatchedAttributes.MarkPathDirty(StageKey);

            double days = SelfResolveDays(stage);
            target.WatchedAttributes.SetDouble(EndDayKey, days > 0.0 ? target.World.Calendar.TotalDays + days : 0.0);
            target.WatchedAttributes.MarkPathDirty(EndDayKey);

            OpenWindow(target);

            if (stage == Stage.None)
            {
                RemoveDebuffs(target);
                StopTicking(target);
            }
            else
            {
                ApplyStageDebuff(target, stage);
                StartTicking(target);
            }
        }

        private static double SelfResolveDays(Stage stage)
        {
            switch (stage)
            {
                case Stage.ChestCold: return 3.0;
                case Stage.Bronchitis: return 7.0;
                case Stage.Flu: return 3.0;
                default: return 0.0;
            }
        }

        private static void RemoveDebuffs(EntityAgent target)
        {
            target.Stats.Remove("healingeffectivness", HealStatKey);
            target.Stats.Remove("walkspeed", WalkStatKey);
        }

        private static void ApplyStageDebuff(EntityAgent target, Stage stage)
        {
            RemoveDebuffs(target);

            switch (stage)
            {
                case Stage.Bronchitis:
                    target.Stats.Set("healingeffectivness", HealStatKey, -0.10f, persistent: true);
                    break;
                case Stage.Flu:
                    target.Stats.Set("healingeffectivness", HealStatKey, -0.10f, persistent: true);
                    target.Stats.Set("walkspeed", WalkStatKey, -0.05f, persistent: true);
                    break;
                case Stage.Pneumonia:
                    target.Stats.Set("healingeffectivness", HealStatKey, -0.20f, persistent: true);
                    target.Stats.Set("walkspeed", WalkStatKey, -0.10f, persistent: true);
                    break;
            }
        }

        private void StartTicking(EntityAgent target)
        {
            if (tickListenerId != 0L) return;

            lastExposureCheckHours = target.World.Calendar.TotalHours;
            tickListenerId = target.World.RegisterGameTickListener(dt => OnTick(target), TickIntervalMs);
        }

        private void StopTicking(EntityAgent target)
        {
            if (tickListenerId == 0L) return;

            target.World.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0L;
        }

        private void OnTick(EntityAgent target)
        {
            Stage stage = CurrentStage;
            if (stage == Stage.None)
            {
                StopTicking(target);
                return;
            }

            double endDay = target.WatchedAttributes.GetDouble(EndDayKey, 0.0);
            if (endDay > 0.0 && target.World.Calendar.TotalDays >= endDay)
            {
                SetStage(target, Stage.None);
                return;
            }

            double now = target.World.Calendar.TotalHours;
            double elapsed = System.Math.Max(0.0, now - lastExposureCheckHours);
            lastExposureCheckHours = now;

            if (IsColdAndWet(target))
            {
                ExposureHours += elapsed;
            }

            if (now < target.WatchedAttributes.GetDouble(NextCheckKey, double.MaxValue)) return;

            double exposure = ExposureHours;
            Stage next = stage;
            switch (stage)
            {
                case Stage.ChestCold:
                    if (exposure >= FluExposureHours) next = Stage.Flu;
                    else if (exposure >= BronchitisExposureHours) next = Stage.Bronchitis;
                    break;
                case Stage.Bronchitis:
                case Stage.Flu:
                    if (exposure >= PneumoniaExposureHours) next = Stage.Pneumonia;
                    break;
            }

            if (next != stage) SetStage(target, next);
            else OpenWindow(target);
        }

        private void OpenWindow(EntityAgent target)
        {
            IGameCalendar calendar = target.World.Calendar;
            double hoursUntilMorning = (MorningHour - calendar.HourOfDay + 24.0) % 24.0;
            if (hoursUntilMorning <= 0.0) hoursUntilMorning = 24.0;

            ExposureHours = 0.0;
            lastExposureCheckHours = calendar.TotalHours;
            target.WatchedAttributes.SetDouble(NextCheckKey, calendar.TotalHours + hoursUntilMorning);
        }

        private static bool IsColdAndWet(EntityAgent target)
        {
            EntityBehaviorBodyTemperature bodyTemperature = target.GetBehavior<EntityBehaviorBodyTemperature>();
            if (bodyTemperature == null) return false;

            return target.WatchedAttributes.GetFloat(FreezingKey, 0f) > 0f && bodyTemperature.Wetness > WetnessThreshold;
        }
    }
}
