using System;
using System.Collections.Generic;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Server-side player-to-player Chest Cold contagion. A (sick source, healthy target) pair rolls
    /// only while both players stand in the same enclosed room (per RoomRegistry) and within
    /// MaxDistance blocks of each other; outdoors or in an open area never counts as shared. Players in
    /// creative or spectator mode neither catch nor spread it.
    ///
    /// One roll per RollIntervalSeconds of in-game time per eligible pair, at BaseChance scaled by
    /// the target's "contagionresistance" stat (1.0 when unset). In-game time is read from the
    /// calendar delta each tick, so rolls follow calendar speed. At most MaxRollsPerTick rolls run
    /// per tick and the surplus is discarded, so a large calendar jump (sleeping) cannot make
    /// infection a certainty. Sources are snapshotted before rolling, so a player infected this
    /// tick cannot infect anyone in the same tick. The calendar does not exist yet when Start runs,
    /// so the first tick only records the baseline hour.
    /// </summary>
    public static class ChestColdContagionSystem
    {
        private const int TickIntervalMs = 1000;
        private const double RollIntervalSeconds = 30.0;
        private const double BaseChance = 0.05;
        private const double MaxDistance = 2.5;
        private const int MaxRollsPerTick = 4;
        private const string ResistanceStat = "contagionresistance";

        private static long listenerId;
        private static double lastTotalHours;
        private static double pendingSeconds;

        private static ICoreServerAPI serverApi;

        public static void Start(ICoreServerAPI api)
        {
            if (listenerId != 0) return;

            serverApi = api;
            lastTotalHours = double.NaN;
            pendingSeconds = 0.0;
            listenerId = api.World.RegisterGameTickListener(dt => OnTick(api), TickIntervalMs);
        }

        public static void Stop()
        {
            if (listenerId == 0) return;

            serverApi.World.UnregisterGameTickListener(listenerId);
            listenerId = 0;
            serverApi = null;
        }

        /// <summary>
        /// Runs up to maxRolls transmission rolls against the target as if a sick player were in the
        /// same room within range, skipping the room and distance checks. Stops at the first
        /// success. Returns whether the target caught Chest Cold, how many rolls ran, and the
        /// per-roll chance after the target's contagionresistance. A target who is already sick
        /// runs no rolls.
        /// </summary>
        public static (bool infected, int rollsRun, double chance) SimulateSickRoommate(EntityPlayer target, int maxRolls)
        {
            EntityBehaviorChestColdFamily behavior = target.GetBehavior<EntityBehaviorChestColdFamily>();
            double chance = BaseChance * target.Stats.GetBlended(ResistanceStat);
            if (behavior == null || behavior.CurrentStage != EntityBehaviorChestColdFamily.Stage.None)
            {
                return (false, 0, chance);
            }

            for (int i = 1; i <= maxRolls; i++)
            {
                if (target.World.Rand.NextDouble() < chance)
                {
                    behavior.TriggerChestCold(target);
                    return (true, i, chance);
                }
            }

            return (false, maxRolls, chance);
        }

        private static void OnTick(ICoreServerAPI api)
        {
            if (api.World.Calendar == null) return;

            double nowHours = api.World.Calendar.TotalHours;
            if (!double.IsNaN(lastTotalHours))
            {
                pendingSeconds += Math.Max(0.0, nowHours - lastTotalHours) * 3600.0;
            }
            lastTotalHours = nowHours;

            int due = (int)Math.Min(pendingSeconds / RollIntervalSeconds, int.MaxValue);
            if (due <= 0) return;
            pendingSeconds -= due * RollIntervalSeconds;
            int rolls = Math.Min(due, MaxRollsPerTick);

            List<IServerPlayer> sources = new List<IServerPlayer>();
            List<IServerPlayer> targets = new List<IServerPlayer>();
            foreach (IPlayer player in api.World.AllOnlinePlayers)
            {
                if (!(player is IServerPlayer serverPlayer)) continue;
                if (serverPlayer.ConnectionState != EnumClientState.Playing) continue;
                EnumGameMode gameMode = serverPlayer.WorldData.CurrentGameMode;
                if (gameMode == EnumGameMode.Spectator || gameMode == EnumGameMode.Creative) continue;

                EntityPlayer entity = serverPlayer.Entity;
                if (entity == null || !entity.Alive) continue;

                EntityBehaviorChestColdFamily behavior = entity.GetBehavior<EntityBehaviorChestColdFamily>();
                if (behavior == null) continue;

                if (behavior.CurrentStage == EntityBehaviorChestColdFamily.Stage.None) targets.Add(serverPlayer);
                else sources.Add(serverPlayer);
            }

            if (sources.Count == 0 || targets.Count == 0) return;

            RoomRegistry rooms = api.ModLoader.GetModSystem<RoomRegistry>();
            if (rooms == null) return;

            foreach (IServerPlayer target in targets)
            {
                EntityPlayer targetEntity = target.Entity;
                double chance = BaseChance * targetEntity.Stats.GetBlended(ResistanceStat);
                if (chance <= 0.0) continue;

                Room targetRoom = null;
                foreach (IServerPlayer source in sources)
                {
                    EntityPlayer sourceEntity = source.Entity;
                    if (sourceEntity.Pos.DistanceTo(targetEntity.Pos) > MaxDistance) continue;

                    targetRoom ??= GetEnclosedRoom(rooms, targetEntity);
                    if (targetRoom == null) break;

                    Room sourceRoom = GetEnclosedRoom(rooms, sourceEntity);
                    if (!IsSameRoom(sourceRoom, targetRoom)) continue;

                    for (int i = 0; i < rolls; i++)
                    {
                        if (api.World.Rand.NextDouble() >= chance) continue;

                        targetEntity.GetBehavior<EntityBehaviorChestColdFamily>().TriggerChestCold(targetEntity);
                        break;
                    }

                    if (targetEntity.GetBehavior<EntityBehaviorChestColdFamily>().CurrentStage
                        != EntityBehaviorChestColdFamily.Stage.None) break;
                }
            }
        }

        // RoomRegistry never returns null: outdoors it yields an open region (ExitCount > 0). Only
        // enclosed rooms count, using vanilla's own enclosure test from EntityBehaviorBodyTemperature.
        private static Room GetEnclosedRoom(RoomRegistry rooms, EntityPlayer entity)
        {
            Room room = rooms.GetRoomForPosition(entity.Pos.AsBlockPos);
            if (room == null) return null;

            return room.ExitCount == 0 || room.SkylightCount < room.NonSkylightCount ? room : null;
        }

        private static bool IsSameRoom(Room a, Room b)
        {
            if (a == null || b == null) return false;

            return ReferenceEquals(a, b) || a.Location.Equals(b.Location);
        }
    }
}
