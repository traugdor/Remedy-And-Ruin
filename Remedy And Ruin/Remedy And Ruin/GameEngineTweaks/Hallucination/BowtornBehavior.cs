using ClientEntityAILib;
using ClientEntityAILib.Pathfinding;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks.Hallucination
{
    /// <summary>
    /// Bowtorn's AI flow: spawns an entity invisibly (ClientEntityAILib's startHidden - it never
    /// renders and there is no reveal step), plays its own real windup sound - creature/bowtorn/
    /// draw, the sound the real creature makes drawing its bow before firing - from wherever it
    /// spawned, then despawns after a short wait. HallucinationManager computes that spawn point
    /// (roughly 20 blocks behind the player) before constructing this, the same way it does for
    /// Drifter/Shiver.
    /// </summary>
    internal class BowtornBehavior : IApparitionBehavior
    {
        private const float DespawnDelaySeconds = 5.0f;

        // Verified against assets/survival/sounds/creature/bowtorn/: draw.ogg is the real windup
        // sound played before the creature fires (release.ogg is the shot itself). The "sounds/"
        // prefix is required - vanilla's own code always adds it before using a raw sound asset
        // path (e.g. AiTaskShootAtEntityConfig.Init(): ShootSound.WithPathPrefixOnce("sounds/")).
        private const string WindupSoundCode = "sounds/creature/bowtorn/draw";

        private readonly ClientControlledEntity entity;
        private readonly bool spawned;
        private float despawnTimer;

        public bool RequestedDespawn { get; private set; }

        public BowtornBehavior(ICoreClientAPI capi, string entityCode, Vec3d spawnPos)
        {
            entity = new ClientControlledEntity(capi, MovementType.CanWalk);
            spawned = entity.SpawnClientCustom(entityCode, spawnPos, new AnimationKeycodes { Idle = "idle" }, startHidden: true);

            if (!spawned)
            {
                RequestedDespawn = true; // bad entity code or handle already spawned - let the manager despawn this immediately
                return;
            }

            entity.PlaySound(WindupSoundCode);
            despawnTimer = DespawnDelaySeconds;
        }

        public void Tick(float dt, Vec3d playerPos)
        {
            if (!spawned || RequestedDespawn) return;

            despawnTimer -= dt;
            if (despawnTimer <= 0f) RequestedDespawn = true;
        }

        public void Dispose()
        {
            entity.Despawn();
        }
    }
}
