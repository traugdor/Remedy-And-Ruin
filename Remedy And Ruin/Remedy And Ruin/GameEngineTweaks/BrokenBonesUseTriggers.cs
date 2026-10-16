using System;
using System.Collections.Generic;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Detects the server-side use events that stress an injured limb and hands each to
    /// EntityBehaviorBrokenBones.RollFromUse, which owns the who-may-roll policy. Hit-escalation and
    /// hard landings are not handled here (see HandlePostArmorDamage).
    ///
    /// Arms: OnPlayerInteractEntity with mode 0 (attack, raised once per attack packet), DidBreakBlock
    /// and DidPlaceBlock (once per block) roll ArmUse for the acting player.
    ///
    /// Legs: per player, EntityPlayer.OnFootStep (raised on the server from the player's walk cycle,
    /// driven by the server-side controls) counts steps and rolls Footstep each time the count reaches
    /// StepsPerRoll; IServerPlayer.InWorldAction rolls Jump on a jump key press while the player is on
    /// the ground, not swimming, flying, noclipping or mounted. The step count is in memory only and
    /// restarts when the Legs tier or splint state changes.
    ///
    /// Start / Stop: subscribe the server events and unsubscribe everything, including every
    /// per-player hook. Players are hooked at PlayerNowPlaying (any older hook for the same player is
    /// removed first) and unhooked at PlayerDisconnect.
    ///
    /// StepsPerRoll: 10 for a Strain leg, halved for each higher tier (rounded up), halved while
    /// sprinting, doubled while sneaking, never below 1; DebugStepsPerRoll replaces the result.
    /// </summary>
    public static class BrokenBonesUseTriggers
    {
        private const int BaseUnsplintedSteps = 10;

        private sealed class PlayerHook
        {
            public EntityPlayer Entity;
            public IServerPlayer Player;
            public Action FootStep;
            public OnEntityAction InWorldAction;
            public int Steps;
            public InjuryTier LastTier;
            public bool LastSplinted;
        }

        private static ICoreServerAPI serverApi;
        private static readonly Dictionary<string, PlayerHook> hooks = new Dictionary<string, PlayerHook>();

        public static void Start(ICoreServerAPI api)
        {
            Stop();
            serverApi = api;
            api.Event.OnPlayerInteractEntity += OnInteractEntity;
            api.Event.DidBreakBlock += OnBlockBroken;
            api.Event.DidPlaceBlock += OnBlockPlaced;
            api.Event.PlayerNowPlaying += HookPlayer;
            api.Event.PlayerDisconnect += UnhookPlayer;
        }

        public static void Stop()
        {
            if (serverApi != null)
            {
                serverApi.Event.OnPlayerInteractEntity -= OnInteractEntity;
                serverApi.Event.DidBreakBlock -= OnBlockBroken;
                serverApi.Event.DidPlaceBlock -= OnBlockPlaced;
                serverApi.Event.PlayerNowPlaying -= HookPlayer;
                serverApi.Event.PlayerDisconnect -= UnhookPlayer;
            }
            serverApi = null;

            foreach (PlayerHook hook in new List<PlayerHook>(hooks.Values)) Detach(hook);
            hooks.Clear();
        }

        // Footsteps to count before one Footstep roll, for a leg at the given tier.
        public static int StepsPerRoll(InjuryTier legTier, bool sprinting, bool sneaking)
        {
            if (EntityBehaviorBrokenBones.DebugStepsPerRoll.HasValue) return Math.Max(1, EntityBehaviorBrokenBones.DebugStepsPerRoll.Value);

            double steps = BaseUnsplintedSteps;
            for (int tier = (int)InjuryTier.Fracture; tier <= (int)legTier; tier++) steps /= 2;
            if (sprinting) steps /= 2;
            if (sneaking) steps *= 2;
            return Math.Max(1, (int)Math.Ceiling(steps));
        }

        private static void HookPlayer(IServerPlayer player)
        {
            UnhookPlayer(player);
            if (player?.Entity == null) return;

            PlayerHook hook = new PlayerHook { Entity = player.Entity, Player = player };
            hook.FootStep = () => OnFootStep(hook);
            hook.InWorldAction = (EnumEntityAction action, bool on, ref EnumHandling handling) => OnWorldAction(hook, action, on);
            hooks[player.PlayerUID] = hook;

            hook.Entity.OnFootStep += hook.FootStep;
            player.InWorldAction += hook.InWorldAction;
        }

        private static void UnhookPlayer(IServerPlayer player)
        {
            if (player?.PlayerUID == null) return;
            if (!hooks.TryGetValue(player.PlayerUID, out PlayerHook hook)) return;

            Detach(hook);
            hooks.Remove(player.PlayerUID);
        }

        private static void Detach(PlayerHook hook)
        {
            hook.Entity.OnFootStep -= hook.FootStep;
            hook.Player.InWorldAction -= hook.InWorldAction;
        }

        private static void OnFootStep(PlayerHook hook)
        {
            EntityBehaviorBrokenBones bones = hook.Entity.GetBehavior<EntityBehaviorBrokenBones>();
            if (bones == null) return;

            InjuryTier tier = bones.GetTier(BoneLocation.Legs);
            bool splinted = bones.IsSplinted(BoneLocation.Legs);
            if (tier != hook.LastTier || splinted != hook.LastSplinted)
            {
                hook.LastTier = tier;
                hook.LastSplinted = splinted;
                hook.Steps = 0;
            }
            if (tier == InjuryTier.None || splinted) return;
            if (hook.Entity.MountedOn != null) return;

            EntityControls controls = hook.Entity.ServerControls;
            hook.Steps++;
            if (hook.Steps < StepsPerRoll(tier, controls.Sprint, controls.Sneak)) return;

            hook.Steps = 0;
            bones.RollFromUse(UseKind.Footstep);
        }

        private static void OnWorldAction(PlayerHook hook, EnumEntityAction action, bool on)
        {
            if (action != EnumEntityAction.Jump || !on) return;

            EntityPlayer entity = hook.Entity;
            EntityControls controls = entity.ServerControls;
            if (!entity.OnGround || entity.Swimming || entity.MountedOn != null || controls.IsFlying || controls.NoClip) return;

            entity.GetBehavior<EntityBehaviorBrokenBones>()?.RollFromUse(UseKind.Jump);
        }

        private static void OnInteractEntity(Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hitPosition, int mode, ref EnumHandling handling)
        {
            if (mode != 0) return;

            RollArmUse(byPlayer);
        }

        private static void OnBlockBroken(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel) => RollArmUse(byPlayer);

        private static void OnBlockPlaced(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel, ItemStack withItemStack) => RollArmUse(byPlayer);

        private static void RollArmUse(IPlayer player) =>
            player?.Entity?.GetBehavior<EntityBehaviorBrokenBones>()?.RollFromUse(UseKind.ArmUse);
    }
}
