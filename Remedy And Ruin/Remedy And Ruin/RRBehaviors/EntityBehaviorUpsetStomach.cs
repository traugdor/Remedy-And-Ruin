using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Remedy_And_Ruin.GameEngineTweaks;

namespace Remedy_And_Ruin.RRBehaviors
{
    /// <summary>
    /// Owns Upset Stomach episodes and food poisoning. Server only; all times are calendar total
    /// hours stored in WatchedAttributes and checked lazily from one tick listener, so both
    /// conditions follow calendar speed and sleep and survive saves and relogs.
    ///
    /// Episode: every trigger vomits a partial void (a fraction of current saturation) and opens a
    /// 5 in-game minute "stomach still settling" window. Two relapse paths each vomit a complete
    /// void and reset the window: a 25% chance, rolled once per partial void, that fires at a random
    /// moment inside the window, and eating anything while the window is open. Only one episode
    /// exists at a time; a new trigger replaces the window and the pending relapse.
    ///
    /// Antinausea: while any Antinausea dose is active the passive relapse chance is 0. A
    /// Concentrated dose also closes the settling window and keeps new episodes from opening one.
    ///
    /// Food poisoning: lasts 24 in-game hours, has no cure and spreads to no one. The sick player
    /// starts a new episode at a random interval between the configured minimum and maximum
    /// in-game minutes. While a regular Antinausea dose is active the interval is doubled; while a
    /// Concentrated one is active the vomiting is skipped and the illness runs on.
    ///
    /// Death clears everything. ResumeAfterLogin restores the tick listener from the stored state.
    /// </summary>
    public class EntityBehaviorUpsetStomach : EntityBehavior
    {
        private const string SettlingEndKey = "remedyandruinStomachSettlingEndHours";
        private const string RelapseAtKey = "remedyandruinStomachRelapseAtHours";
        private const string PoisonEndKey = "remedyandruinFoodPoisoningEndHours";
        private const string PoisonNextVomitKey = "remedyandruinFoodPoisoningNextVomitHours";

        public const double SettlingWindowHours = 5.0 / 60.0;
        public const double FoodPoisoningHours = 24.0;
        private const double PassiveRelapseChance = 0.25;
        private const double PartialVoidMinFraction = 0.25;
        private const double PartialVoidMaxFraction = 0.5;
        private const int TickIntervalMs = 1000;

        // Lets the vanilla eat step finish crediting its saturation before a food-triggered void
        // drains it.
        private const int PostEatVoidDelayMs = 250;

        private long tickListenerId;

        public EntityBehaviorUpsetStomach(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinUpsetStomach";

        private bool IsServer => entity.World.Side == EnumAppSide.Server;
        private double Now => entity.World.Calendar.TotalHours;

        private double GetHours(string key) => entity.WatchedAttributes.GetDouble(key, 0.0);

        private void SetHours(string key, double value)
        {
            entity.WatchedAttributes.SetDouble(key, value);
            entity.WatchedAttributes.MarkPathDirty(key);
        }

        public bool IsSettling => GetHours(SettlingEndKey) > Now;

        public bool HasFoodPoisoning => GetHours(PoisonEndKey) > Now;

        public double SettlingHoursRemaining => Math.Max(0.0, GetHours(SettlingEndKey) - Now);

        public double FoodPoisoningHoursRemaining => Math.Max(0.0, GetHours(PoisonEndKey) - Now);

        /// <summary>In-game hours until the pending passive relapse, or null when none is pending.</summary>
        public double? RelapseHoursRemaining
        {
            get
            {
                double at = GetHours(RelapseAtKey);
                return at > 0.0 && IsSettling ? Math.Max(0.0, at - Now) : (double?)null;
            }
        }

        /// <summary>In-game hours until the next food poisoning vomit, or null when not poisoned.</summary>
        public double? NextVomitHoursRemaining =>
            HasFoodPoisoning ? Math.Max(0.0, GetHours(PoisonNextVomitKey) - Now) : (double?)null;

        private PotionLevel AntinauseaLevel =>
            PotionEffects.ActivePotionLevel(entity, EntityBehaviorRemedyEffects.EffectCluster.ANTINAUSEA);

        // ---------------------------------------------------------------- triggers

        /// <summary>
        /// Starts an Upset Stomach episode: a partial void, or a complete one when
        /// completeVoid is set (Antiseptic overdose, debug). Replaces any episode in progress.
        /// </summary>
        public void TriggerUpsetStomach(bool completeVoid = false) => BeginEpisode(completeVoid, deferVoid: false);

        /// <summary>One complete void at once, with no settling window and no relapse after it.</summary>
        public void VoidCompletely()
        {
            if (!IsServer || !entity.Alive) return;

            Vomit(1.0, deferVoid: false);
        }

        /// <summary>
        /// Starts food poisoning for 24 in-game hours. Does nothing if the player already has it,
        /// so a second contaminated meal never extends or stacks the illness.
        /// </summary>
        public void StartFoodPoisoning()
        {
            if (!IsServer || !entity.Alive || HasFoodPoisoning) return;

            SetHours(PoisonEndKey, Now + FoodPoisoningHours);
            ScheduleNextPoisonVomit();
            Notify("remedyandruin:stomach-foodpoisoning");
            EnsureTicking();
        }

        /// <summary>
        /// Called once per completed eating or drinking action that delivered real food.
        /// spoilageLevel is the food's Perish TransitionLevel (0 to 1) and is the chance it makes
        /// the player sick at all; a second flip at the same level picks food poisoning over a
        /// single vomit. isAlcohol skips that roll. Eating while the settling window is open is a
        /// relapse whatever the food. One eating action never causes two voids.
        /// </summary>
        public void OnFoodConsumed(float spoilageLevel, bool isAlcohol)
        {
            if (!IsServer || !entity.Alive) return;

            bool settling = IsSettling;
            bool sick = false;
            bool poisoned = false;
            if (!isAlcohol && spoilageLevel > 0f)
            {
                double level = Math.Min(1.0, spoilageLevel);
                sick = entity.World.Rand.NextDouble() < level;
                poisoned = sick && entity.World.Rand.NextDouble() < level;
            }

            if (poisoned) StartFoodPoisoning();

            if (settling) Relapse(deferVoid: true);
            else if (sick && !poisoned) BeginEpisode(completeVoid: false, deferVoid: true);
        }

        /// <summary>Applies a drunk Antinausea dose (Regular or Concentrated) to any current episode or food poisoning.</summary>
        public void OnAntinauseaDrunk(PotionLevel level)
        {
            if (!IsServer || !entity.Alive) return;

            SetHours(RelapseAtKey, 0.0);
            if (level == PotionLevel.Concentrated) SetHours(SettlingEndKey, 0.0);
            if (HasFoodPoisoning && level == PotionLevel.Regular) ScheduleNextPoisonVomit();
        }

        /// <summary>Clears the episode, the settling window and food poisoning.</summary>
        public void ClearAll()
        {
            SetHours(SettlingEndKey, 0.0);
            SetHours(RelapseAtKey, 0.0);
            SetHours(PoisonEndKey, 0.0);
            SetHours(PoisonNextVomitKey, 0.0);
            StopTicking();
        }

        // ---------------------------------------------------------------- episode mechanics

        private void BeginEpisode(bool completeVoid, bool deferVoid)
        {
            if (!IsServer || !entity.Alive) return;

            double fraction = completeVoid
                ? 1.0
                : PartialVoidMinFraction + entity.World.Rand.NextDouble() * (PartialVoidMaxFraction - PartialVoidMinFraction);
            Vomit(fraction, deferVoid);
            OpenSettlingWindow();

            bool relapses = !completeVoid
                && AntinauseaLevel == PotionLevel.None
                && entity.World.Rand.NextDouble() < PassiveRelapseChance
                && IsSettling;
            SetHours(RelapseAtKey, relapses ? Now + entity.World.Rand.NextDouble() * SettlingHoursRemaining : 0.0);

            EnsureTicking();
        }

        private void Relapse(bool deferVoid)
        {
            Vomit(1.0, deferVoid);
            OpenSettlingWindow();
            SetHours(RelapseAtKey, 0.0);
            EnsureTicking();
        }

        private void OpenSettlingWindow()
        {
            SetHours(SettlingEndKey, AntinauseaLevel == PotionLevel.Concentrated ? 0.0 : Now + SettlingWindowHours);
        }

        private void Vomit(double fraction, bool deferVoid)
        {
            if (deferVoid)
            {
                entity.World.RegisterCallback(dt =>
                {
                    if (entity.Alive) PerformVomit(fraction);
                }, PostEatVoidDelayMs);
            }
            else
            {
                PerformVomit(fraction);
            }
        }

        private void PerformVomit(double fraction)
        {
            entity.GetBehavior<EntityBehaviorRemedyEffects>()?.VoidStomach(fraction);
            Notify("remedyandruin:stomach-vomit");
        }

        // ---------------------------------------------------------------- food poisoning

        private void ScheduleNextPoisonVomit()
        {
            ConfigServer config = Remedy_And_RuinModSystem.Config;
            double minMinutes = Math.Max(1.0, config?.foodPoisoningVomitMinIntervalMinutes ?? 20.0);
            double maxMinutes = Math.Max(minMinutes, config?.foodPoisoningVomitMaxIntervalMinutes ?? 60.0);
            double minutes = minMinutes + entity.World.Rand.NextDouble() * (maxMinutes - minMinutes);
            if (AntinauseaLevel == PotionLevel.Regular) minutes *= 2.0;

            SetHours(PoisonNextVomitKey, Now + minutes / 60.0);
        }

        private void EndFoodPoisoning()
        {
            SetHours(PoisonEndKey, 0.0);
            SetHours(PoisonNextVomitKey, 0.0);
            Notify("remedyandruin:stomach-foodpoisoning-ended");
        }

        // ---------------------------------------------------------------- ticking

        private void EnsureTicking()
        {
            if (tickListenerId != 0L) return;

            tickListenerId = entity.World.RegisterGameTickListener(OnTick, TickIntervalMs);
        }

        private void StopTicking()
        {
            if (tickListenerId == 0L) return;

            entity.World.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0L;
        }

        private void OnTick(float dt)
        {
            if (!entity.Alive)
            {
                ClearAll();
                return;
            }

            double now = Now;

            double relapseAt = GetHours(RelapseAtKey);
            if (relapseAt > 0.0)
            {
                if (now >= GetHours(SettlingEndKey)) SetHours(RelapseAtKey, 0.0);
                else if (now >= relapseAt) Relapse(deferVoid: false);
            }

            double poisonEnd = GetHours(PoisonEndKey);
            if (poisonEnd > 0.0)
            {
                if (now >= poisonEnd)
                {
                    EndFoodPoisoning();
                }
                else if (now >= GetHours(PoisonNextVomitKey))
                {
                    bool suppressed = AntinauseaLevel == PotionLevel.Concentrated;
                    ScheduleNextPoisonVomit();
                    if (!suppressed) BeginEpisode(completeVoid: false, deferVoid: false);
                }
            }

            double settlingEnd = GetHours(SettlingEndKey);
            if (settlingEnd > 0.0 && now >= settlingEnd) SetHours(SettlingEndKey, 0.0);

            if (GetHours(SettlingEndKey) <= 0.0 && GetHours(RelapseAtKey) <= 0.0 && GetHours(PoisonEndKey) <= 0.0)
            {
                StopTicking();
            }
        }

        /// <summary>Re-registers the tick listener after a login when stored state is still pending.</summary>
        public void ResumeAfterLogin(EntityAgent target)
        {
            if (target.World.Side != EnumAppSide.Server) return;

            if (!entity.Alive)
            {
                ClearAll();
                return;
            }

            if (GetHours(SettlingEndKey) > 0.0 || GetHours(RelapseAtKey) > 0.0 || GetHours(PoisonEndKey) > 0.0)
            {
                EnsureTicking();
            }
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);
            if (!IsServer) return;

            ClearAll();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            StopTicking();
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// True for food that actually feeds: nutrition properties exist and are not the NoNutrition
        /// category. Remedy potions carry nutrition properties of that category, so they never count.
        /// </summary>
        public static bool IsRealFood(FoodNutritionProperties props) =>
            props != null && props.FoodCategory != EnumFoodCategory.NoNutrition;

        /// <summary>Human-readable state for the /rrStomach command.</summary>
        public List<string> Describe()
        {
            var lines = new List<string>();
            lines.Add(IsSettling
                ? $"Stomach settling: {SettlingHoursRemaining * 60.0:0.0} in-game minutes remaining"
                    + (RelapseHoursRemaining is double relapse ? $", passive relapse in {relapse * 60.0:0.0} in-game minutes" : ", no relapse pending")
                : "Stomach settling: none");
            lines.Add(HasFoodPoisoning
                ? $"Food poisoning: {FoodPoisoningHoursRemaining:0.00} in-game hours remaining, next vomit in {(NextVomitHoursRemaining ?? 0.0) * 60.0:0.0} in-game minutes"
                : "Food poisoning: none");
            PotionLevel antinausea = AntinauseaLevel;
            if (antinausea != PotionLevel.None) lines.Add($"Antinausea active: {antinausea}");
            return lines;
        }

        private void Notify(string langKey)
        {
            if (entity is not EntityPlayer player || player.Player is not IServerPlayer serverPlayer) return;

            string text = Lang.Get(langKey);
            serverPlayer.SendIngameError("remedyandruinStomach", text);
            if (Remedy_And_RuinModSystem.Config?.injuryChatMessagesEnabled ?? true)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, text, EnumChatType.Notification);
            }
        }
    }
}
