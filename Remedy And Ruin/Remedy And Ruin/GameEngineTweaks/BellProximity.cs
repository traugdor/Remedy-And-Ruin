using System;
using System.Collections.Generic;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Server-side detection of exposure to a ringing bell, which starts the Bell headache owned by
    /// EntityBehaviorBrokenBones. EntityBell exposes no ringing field; a bell rings while its
    /// EntityBehaviorTaskAI has an active AiTaskBellAlarm.
    ///
    /// A player is exposed while at least one ringing EntityBell is within ExposureRange blocks
    /// (horizontally and vertically), matching the bell task's seekingRange. Players in creative or
    /// spectator mode and dead players are never exposed.
    ///
    /// Accumulator: per player, in memory only. Each tick adds the in-game seconds elapsed (calendar
    /// TotalHours delta times 3600, so it follows calendar speed) while exposed, capped at
    /// MaxSecondsPerTick so a sleep or time jump cannot grant instant exposure, and drops to 0 on any
    /// tick without exposure. Reaching TriggerSeconds starts the headache. The calendar does not
    /// exist yet when Start runs, so the first tick only records the baseline hour.
    /// </summary>
    public static class BellProximity
    {
        private const int TickIntervalMs = 1000;
        private const float ExposureRange = 15f;
        private const double TriggerSeconds = 300.0;
        private const double MaxSecondsPerTick = 120.0;

        private static long listenerId;
        private static double lastTotalHours;
        private static ICoreServerAPI serverApi;
        private static readonly Dictionary<string, double> exposureSeconds = new Dictionary<string, double>();
        private static readonly HashSet<string> seen = new HashSet<string>();

        // Debug watch: players who asked for a periodic report of why they are or are not exposed.
        private const int WatchLogEveryTicks = 2;
        private static readonly Dictionary<string, string> watching = new Dictionary<string, string>();
        private static int watchTick;

        /// <summary>Turns the watch on or off for a player; returns the new state.</summary>
        public static bool SetWatch(string playerUid, bool on)
        {
            if (on) watching[playerUid] = "";
            else watching.Remove(playerUid);
            return on;
        }

        public static bool IsWatching(string playerUid) => watching.ContainsKey(playerUid);

        public static void Start(ICoreServerAPI api)
        {
            if (listenerId != 0) return;

            serverApi = api;
            lastTotalHours = double.NaN;
            exposureSeconds.Clear();
            listenerId = api.World.RegisterGameTickListener(dt => OnTick(api), TickIntervalMs);
        }

        public static void Stop()
        {
            if (listenerId == 0) return;

            serverApi.World.UnregisterGameTickListener(listenerId);
            listenerId = 0;
            serverApi = null;
            exposureSeconds.Clear();
            watching.Clear();
        }

        private static void OnTick(ICoreServerAPI api)
        {
            if (api.World.Calendar == null) return;

            double nowHours = api.World.Calendar.TotalHours;
            double elapsed = double.IsNaN(lastTotalHours) ? 0.0 : Math.Max(0.0, nowHours - lastTotalHours) * 3600.0;
            lastTotalHours = nowHours;
            elapsed = Math.Min(elapsed, MaxSecondsPerTick);

            seen.Clear();
            foreach (IPlayer player in api.World.AllOnlinePlayers)
            {
                if (!(player is IServerPlayer serverPlayer)) continue;
                if (serverPlayer.ConnectionState != EnumClientState.Playing) continue;

                EntityPlayer entity = serverPlayer.Entity;
                EntityBehaviorBrokenBones bones = entity?.GetBehavior<EntityBehaviorBrokenBones>();
                if (bones == null) continue;

                seen.Add(serverPlayer.PlayerUID);
                bool exposed = IsExposed(api, serverPlayer, entity);
                exposureSeconds.TryGetValue(serverPlayer.PlayerUID, out double accumulated);
                accumulated = exposed ? accumulated + elapsed : 0.0;

                if (exposed && accumulated >= TriggerSeconds && !EntityBehaviorBrokenBones.IsBellHeadacheActive(entity))
                {
                    bones.StartBellHeadache();
                }

                bones.TickBellHeadache(exposed);
                exposureSeconds[serverPlayer.PlayerUID] = accumulated;

                if (watching.ContainsKey(serverPlayer.PlayerUID)) ReportWatch(api, serverPlayer, entity, exposed, accumulated);
            }
            watchTick++;

            if (exposureSeconds.Count > seen.Count)
            {
                List<string> stale = new List<string>();
                foreach (string uid in exposureSeconds.Keys)
                {
                    if (!seen.Contains(uid)) stale.Add(uid);
                }
                foreach (string uid in stale) exposureSeconds.Remove(uid);
            }
        }

        // Writes one line to the server log every WatchLogEveryTicks ticks and sends it to the player in
        // chat whenever the in-range or ringing state changes: the game mode, the nearest bell of any
        // state within 64 blocks (distance, ringing or not), whether it is inside ExposureRange, and
        // the accumulated exposure.
        private static void ReportWatch(ICoreServerAPI api, IServerPlayer player, EntityPlayer entity, bool exposed, double accumulated)
        {
            EntityBell nearest = null;
            double nearestDistance = double.MaxValue;
            foreach (Entity candidate in api.World.GetEntitiesAround(entity.Pos.XYZ, 64f, 64f, e => e is EntityBell))
            {
                double distance = candidate.Pos.DistanceTo(entity.Pos.XYZ);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = (EntityBell)candidate;
                }
            }

            string bellText = nearest == null
                ? "no bell within 64 blocks"
                : $"nearest bell {nearestDistance:0.#} blocks away, alive {nearest.Alive}, ringing {IsRinging(nearest)}, in range ({ExposureRange:0}) {nearestDistance <= ExposureRange}";
            string line = $"Bell watch: mode {player.WorldData.CurrentGameMode}, {bellText}, exposed {exposed}, exposure {accumulated:0}/{TriggerSeconds:0} s, headache {(EntityBehaviorBrokenBones.IsBellHeadacheActive(entity) ? "active" : "none")}.";

            if (watchTick % WatchLogEveryTicks == 0) api.Logger.Notification("[RemedyAndRuin] " + line);

            string state = $"{nearest != null}|{nearest != null && IsRinging(nearest)}|{nearest != null && nearestDistance <= ExposureRange}|{exposed}";
            if (watching.TryGetValue(player.PlayerUID, out string last) && last == state) return;

            watching[player.PlayerUID] = state;
            player.SendMessage(GlobalConstants.InfoLogChatGroup, line, EnumChatType.Notification);
        }

        private static bool IsExposed(ICoreServerAPI api, IServerPlayer player, EntityPlayer entity)
        {
            if (!entity.Alive) return false;
            EnumGameMode mode = player.WorldData.CurrentGameMode;
            if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator) return false;

            Entity[] bells = api.World.GetEntitiesAround(entity.Pos.XYZ, ExposureRange, ExposureRange, IsRinging);
            return bells.Length > 0;
        }

        private static bool IsRinging(Entity candidate) =>
            candidate is EntityBell bell
            && bell.Alive
            && (bell.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.IsTaskActive<AiTaskBellAlarm>() ?? false);
    }
}
