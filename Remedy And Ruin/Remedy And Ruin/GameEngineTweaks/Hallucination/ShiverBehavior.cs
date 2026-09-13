using System;
using ClientEntityAILib;
using ClientEntityAILib.Pathfinding;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks.Hallucination
{
    /// <summary>
    /// Shiver's AI flow, driven through ClientEntityAILib's ClientControlledEntity rather than
    /// HallucinationApparition's own manual entity/movement primitives - real obstacle-avoiding
    /// pathfinding for both the wander legs and the beeline (both via the MoveToSlow/MoveToFast
    /// callback overloads), real per-entity-derived speeds, and terrain-following, all handled by
    /// the library. This class only owns the AI decisions: which node to wander to next, how long
    /// to pause, when to switch to beelining, and when close enough to the player to attack. Kept
    /// fully separate from DrifterBehavior (rather than sharing a base class) so drifter-only
    /// additions don't touch this flow at all.
    /// </summary>
    internal class ShiverBehavior : IApparitionBehavior
    {
        private const int WanderNodeCountMin = 3;
        private const int WanderNodeCountMax = 4;
        private const double MinNodeDistance = 5.0;
        private const double MaxNodeDistance = 9.0;
        private const float MinPauseSeconds = 1f;
        private const float MaxPauseSeconds = 4f;
        private const double AttackRange = 2.0;

        // Any single-frame move past this is implausible for actual movement at this entity's
        // speed tiers - only a teleport/re-snap could produce it. Diagnostic only.
        private const double TeleportJumpThreshold = 2.0;

        // Matches attackDurationMs: 1000 on the real shiver's meleeattack AI task
        // (assets/survival/entities/lore/shiver.json).
        private const float AttackDurationSeconds = 1.0f;

        // How often the beeline re-issues a fresh obstacle-avoiding pathfind toward the player's
        // current position. A single MoveToFast(callback) call resolves against a fixed snapshot
        // of the target and doesn't replan as the player moves, so this has to be re-issued
        // periodically rather than once - but the search runs on a background thread, so there's
        // no reason to re-request more often than the player can meaningfully move.
        private const float BeelineRetargetInterval = 1.0f;

        // Verified against the real shiver's own AI tasks in that same JSON: wander uses
        // animation "walk", seekentity/fleeentity use "run", meleeattack uses "bite".
        private const string IdleAnimCode = "idle";
        private const string WalkAnimCode = "walk";
        private const string RunAnimCode = "run";
        private const string AttackAnimCode = "bite";

        private static int nextInstanceId = 0;
        private readonly int instanceId = nextInstanceId++;

        private readonly ICoreClientAPI capi;
        private readonly ClientControlledEntity entity;
        private readonly Random rand = new Random();
        private readonly bool spawned;

        private enum State { Wandering, Paused, Beelining, Attacking, Done }
        private State state;
        private readonly Vec3d[] wanderNodes;
        private int wanderNodeIndex;
        private float pauseTimer;
        private float attackTimer;
        private float beelineRetargetTimer;
        private Vec3d lastLoggedPos;

        public bool RequestedDespawn => state == State.Done;

        public ShiverBehavior(ICoreClientAPI capi, string entityCode, Vec3d spawnPos)
        {
            this.capi = capi;

            // Shivers climb per their own JSON ("canClimb": true).
            entity = new ClientControlledEntity(capi, MovementType.CanWalk | MovementType.CanClimb);
            spawned = entity.SpawnClientCustom(entityCode, spawnPos, new AnimationKeycodes
            {
                Idle = IdleAnimCode,
                MoveSlow = WalkAnimCode,
                MoveFast = RunAnimCode
            });
            capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] spawned={spawned} at {spawnPos}");

            int nodeCount = WanderNodeCountMin + rand.Next(WanderNodeCountMax - WanderNodeCountMin + 1);
            wanderNodes = new Vec3d[nodeCount];
            Vec3d prev = spawnPos;
            for (int i = 0; i < nodeCount; i++)
            {
                double angle = rand.NextDouble() * GameMath.TWOPI;
                double dist = MinNodeDistance + rand.NextDouble() * (MaxNodeDistance - MinNodeDistance);
                Vec3d node = new Vec3d(prev.X + Math.Cos(angle) * dist, spawnPos.Y, prev.Z + Math.Sin(angle) * dist);
                wanderNodes[i] = node;
                prev = node;
            }
            wanderNodeIndex = 0;
            state = State.Wandering;
            capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] wander nodes ({nodeCount}): {string.Join(" | ", Array.ConvertAll(wanderNodes, n => n.ToString()))}");

            if (spawned) GoToCurrentWanderNode();
            else state = State.Done; // bad entity code or handle already spawned - let the manager despawn this immediately
        }

        // Real obstacle-avoiding pathfind, triggered once per node rather than every tick - the
        // callback overload runs a background A* search and fires exactly once on arrival/failure.
        private void GoToCurrentWanderNode()
        {
            Vec3d node = wanderNodes[wanderNodeIndex];
            int nodeIndexForLog = wanderNodeIndex;
            capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] pathfinding to wander node {nodeIndexForLog} at ({node.X:F1}, {node.Z:F1})");
            entity.MoveToSlow(node.X, node.Z, arrived =>
            {
                capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] wander node {nodeIndexForLog} callback: arrived={arrived}, state={state}");
                if (state == State.Done) return;

                if (arrived)
                {
                    state = State.Paused;
                    pauseTimer = MinPauseSeconds + (float)rand.NextDouble() * (MaxPauseSeconds - MinPauseSeconds);
                    capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] pausing {pauseTimer:F2}s at node {nodeIndexForLog}");
                }
                else
                {
                    // No path found to this node - skip it rather than getting stuck forever.
                    AdvanceWanderNode();
                }
            });
        }

        // Diagnostic only: catches any single-frame position jump too large for normal movement
        // at this entity's speed, so a report of visible teleporting can be confirmed (or ruled
        // out) from the log instead of inferred from code reading.
        private void CheckForTeleport()
        {
            Vec3d pos = entity.GetPosition();
            if (pos == null) return;

            if (lastLoggedPos != null)
            {
                double dx = pos.X - lastLoggedPos.X;
                double dz = pos.Z - lastLoggedPos.Z;
                double moved = Math.Sqrt(dx * dx + dz * dz);
                if (moved > TeleportJumpThreshold)
                {
                    capi.Logger.Warning($"remedyandruin: ShiverBehavior[{instanceId}] TELEPORT jump of {moved:F2} blocks in one tick: {lastLoggedPos} -> {pos}");
                }
            }

            lastLoggedPos = pos;
        }

        private void AdvanceWanderNode()
        {
            wanderNodeIndex++;
            if (wanderNodeIndex >= wanderNodes.Length)
            {
                state = State.Beelining;
                capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] wander nodes exhausted, beelining now");
            }
            else
            {
                state = State.Wandering;
                GoToCurrentWanderNode();
            }
        }

        public void Tick(float dt, Vec3d playerPos)
        {
            if (!spawned) return;

            CheckForTeleport();

            switch (state)
            {
                case State.Paused:
                    pauseTimer -= dt;
                    if (pauseTimer <= 0f) AdvanceWanderNode();
                    break;
                case State.Beelining:
                    TickBeelining(dt, playerPos);
                    break;
                case State.Attacking:
                    attackTimer -= dt;
                    if (attackTimer <= 0f) state = State.Done;
                    break;
                // Wandering: nothing to do per-tick - waiting on GoToCurrentWanderNode's callback.
                // Done: HallucinationManager despawns on the next check.
            }
        }

        // Attack-range is checked every tick regardless of retarget cadence, but the actual
        // pathfind re-request only fires on BeelineRetargetInterval - see that constant's comment.
        private void TickBeelining(float dt, Vec3d playerPos)
        {
            // Horizontal-only, matching DrifterBehavior/BowtornBehavior's own attack-range check -
            // DistanceTo is a true 3D distance, so a vertical mismatch between the shiver's
            // grounded Y and the player's Y (a step, a slope) would otherwise inflate this past
            // AttackRange even when the two are standing right next to each other.
            Vec3d pos = entity.GetPosition();
            double dx = playerPos.X - pos.X;
            double dz = playerPos.Z - pos.Z;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist <= AttackRange)
            {
                state = State.Attacking;
                attackTimer = AttackDurationSeconds;
                bool played = entity.PlayOneShotAnimation(AttackAnimCode);
                capi.Logger.Notification($"remedyandruin: ShiverBehavior[{instanceId}] attacking at dist={dist:F2}, PlayOneShotAnimation returned {played}");
                return;
            }

            beelineRetargetTimer -= dt;
            if (beelineRetargetTimer > 0f) return;
            beelineRetargetTimer = BeelineRetargetInterval;

            // Fires exactly once: true = arrived, false = no path found or despawned before
            // arrival. A call superseded by a newer MoveToFast/MoveToSlow gets no callback at
            // all, so an in-flight search from a stale player position is safely discarded the
            // next time this fires - nothing needs to track "is a search in flight."
            entity.MoveToFast(playerPos.X, playerPos.Z, reached => { });
        }

        public void Dispose()
        {
            entity.Despawn();
        }
    }
}
