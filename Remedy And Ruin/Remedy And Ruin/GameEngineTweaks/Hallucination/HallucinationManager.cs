using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks.Hallucination
{
    /// <summary>
    /// Owns every active Hallucination apparition for this client: rolls new spawns and ticks
    /// their AI. Rendering is handled entirely by the client's own real entity-rendering
    /// pipeline, driven through ClientEntityAILib - see DrifterBehavior/ShiverBehavior for how.
    ///
    /// Rolling is gated by getSeverity() - the current Mind Poison ("Brain Rot") strength, 0-1,
    /// supplied by the caller as the stronger of TemporalVignetteRenderer.MindPoisonFogStrength
    /// (the .rrbrainrot debug command's manual sample) and MindPoisonServerSeverity (the real,
    /// tolerance-discounted severity a live MINDPOISON exposure drives via
    /// EntityBehaviorRemedyEffects.StartMindPoisonSeverityContribution's synced bridge) - either
    /// source can trigger spawns independently. No rolling at all while severity is 0; which
    /// families are eligible scales with severity per the design doc's three windows (see
    /// PickFamilyForSeverity).
    ///
    /// Bowtorn spawns invisibly (ClientEntityAILib's startHidden - see BowtornBehavior) roughly 20
    /// blocks behind the player rather than in view like Drifter/Shiver, plays its own real windup
    /// sound, and despawns shortly after - a purely audio scare with nothing to actually see.
    /// Apparition candidates are resolved from the live capi.World.EntityTypes list by Code.Path
    /// prefix, rather than a hardcoded list of base+state codes - the generic block/item/entity
    /// variant-expansion system (RegistryObjectType/ModRegistryObjectTypeLoader) supports a
    /// "skipVariants" exclusion list per entity, so not every combination that looks valid from
    /// the JSON is actually generated, and a hardcoded list would need to track that by hand.
    /// </summary>
    public class HallucinationManager : IRenderer
    {
        private static readonly string[] ApparitionFamilies = { "drifter-", "bowtorn-", "shiver-" };

        private const double BowtornMinDistance = 13.0;
        private const double BowtornMaxDistance = 17.0;

        private const int MaxConcurrent = 5;
        private const float RollIntervalMinSeconds = 20f;
        private const float RollIntervalMaxSeconds = 30f;

        private readonly ICoreClientAPI capi;
        private readonly Func<float> getSeverity;
        private readonly List<IApparitionBehavior> active = new List<IApparitionBehavior>();
        private readonly Random rand = new Random();
        private float secondsToNextRoll;
        private List<EntityProperties> apparitionCandidates;

        // IRenderer here is just a convenient per-frame tick hook (AI movement) - no drawing
        // happens in this class, so the exact order doesn't matter.
        public double RenderOrder => 0.6;
        public int RenderRange => 999;

        public HallucinationManager(ICoreClientAPI capi, Func<float> getSeverity)
        {
            this.capi = capi;
            this.getSeverity = getSeverity;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "remedyandruin-hallucination");
            RollNextInterval();
        }

        private void RollNextInterval()
        {
            secondsToNextRoll = RollIntervalMinSeconds + (float)rand.NextDouble() * (RollIntervalMaxSeconds - RollIntervalMinSeconds);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.IsGamePaused || capi.World?.Player?.Entity == null) return;

            float severity = getSeverity();
            if (severity <= 0f)
            {
                // Mind Poison ended (Antidote, natural expiry, etc.) - clear everything immediately
                // rather than letting each apparition finish its own wander/beeline/attack on its
                // own schedule, which could otherwise leave one wandering around for a while after
                // the player is no longer actually affected.
                if (active.Count > 0)
                {
                    capi.Logger.Notification($"remedyandruin: Hallucination severity dropped to 0, despawning {active.Count} active apparition(s).");
                    foreach (IApparitionBehavior apparition in active) apparition.Dispose();
                    active.Clear();
                }
                return;
            }

            if (active.Count < MaxConcurrent)
            {
                secondsToNextRoll -= deltaTime;
                if (secondsToNextRoll <= 0f)
                {
                    RollNextInterval();
                    SpawnOne(PickFamilyForSeverity(severity));
                }
            }

            Vec3d playerPos = capi.World.Player.Entity.Pos.XYZ;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                IApparitionBehavior apparition = active[i];

                if (apparition.RequestedDespawn)
                {
                    capi.Logger.Notification("remedyandruin: Hallucination apparition despawned - attack finished.");
                    apparition.Dispose();
                    active.RemoveAt(i);
                    continue;
                }

                apparition.Tick(deltaTime, playerPos);
            }
        }

        /// <summary>
        /// Design doc's three severity windows, strictly additive: 0-0.33 Drifter only, 0.33-0.66
        /// adds Shiver, 0.66+ adds Bowtorn - each window keeps every family the previous one had.
        /// </summary>
        private ApparitionFamily PickFamilyForSeverity(float severity)
        {
            List<ApparitionFamily> eligible = new List<ApparitionFamily> { ApparitionFamily.Drifter };
            if (severity >= 1f / 3f) eligible.Add(ApparitionFamily.Shiver);
            if (severity >= 2f / 3f) eligible.Add(ApparitionFamily.Bowtorn);
            return eligible[rand.Next(eligible.Count)];
        }

        /// <summary>
        /// Spawns one apparition: Drifter/Shiver already inside the player's field of view,
        /// Bowtorn invisibly behind them.
        /// </summary>
        private void SpawnOne(ApparitionFamily family)
        {
            EntityPlayer plrEntity = capi.World.Player.Entity;
            Vec3d plrPos = plrEntity.Pos.XYZ;

            if (family == ApparitionFamily.Bowtorn)
            {
                SpawnBowtornBehindPlayer(plrPos);
                return;
            }

            // Kept well inside the field of view (not equal to it) - a spawn placed right at the
            // edge starts with zero margin, so any ordinary mouse drift in the first second
            // pushes it out of view before it's ever actually seen.
            float spreadRad = 15f * GameMath.DEG2RAD;
            float angle = capi.Input.MouseYaw + ((float)rand.NextDouble() * 2f - 1f) * spreadRad;

            BlockPos probe = plrPos.AsBlockPos;
            bool enclosed = capi.World.BlockAccessor.GetRainMapHeightAt(probe) > probe.Y + 1;
            double distance = enclosed
                ? 5.0 + rand.NextDouble() * 5.0
                : 15.0 + rand.NextDouble() * 35.0;

            // EntityPos.GetViewVector(pitch, yaw) is the entity's real forward vector - must
            // include pitch, since cos(pitch) is routinely negative in this engine (its
            // "looking level" pitch value isn't 0), which would flip a yaw-only direction 180
            // degrees from the real camera.
            Vec3f forward = EntityPos.GetViewVector(capi.Input.MousePitch, angle);
            Vec3d camPos = plrEntity.CameraPos;
            double spawnX = camPos.X + forward.X * distance;
            double spawnZ = camPos.Z + forward.Z * distance;

            SpawnApparitionAt(spawnX, spawnZ, plrPos.Y, family);
        }

        /// <summary>
        /// Spawns Bowtorn (invisible - see BowtornBehavior) at a point roughly 20 blocks away,
        /// within the rear half of the player's current facing (never in front, where it'd give
        /// away that it's not a real creature about to be seen).
        /// </summary>
        private void SpawnBowtornBehindPlayer(Vec3d plrPos)
        {
            float rearYaw = capi.Input.MouseYaw + GameMath.PI;
            float angle = rearYaw + ((float)rand.NextDouble() * 2f - 1f) * GameMath.PIHALF;
            double distance = BowtornMinDistance + rand.NextDouble() * (BowtornMaxDistance - BowtornMinDistance);

            Vec3f dir = EntityPos.GetViewVector(0f, angle);
            double spawnX = plrPos.X + dir.X * distance;
            double spawnZ = plrPos.Z + dir.Z * distance;

            SpawnApparitionAt(spawnX, spawnZ, plrPos.Y, ApparitionFamily.Bowtorn);
        }

        /// <summary>
        /// The camera's own forward vector can easily point below the actual ground (e.g.
        /// looking slightly down at a distance), burying the apparition in terrain and pinning
        /// its AI in place. Grounds it near the player's own Y via a local downward scan
        /// (ApparitionTerrain.FindGroundY) rather than a sky heightmap, so this works correctly
        /// underground or indoors too, where a heightmap would return a cave roof or building
        /// roof instead of the actual floor near the player.
        /// </summary>
        private void SpawnApparitionAt(double spawnX, double spawnZ, double anchorY, ApparitionFamily family)
        {
            List<EntityProperties> candidates = GetApparitionCandidates().FindAll(p => FamilyFromCode(p.Code) == family);
            if (candidates.Count == 0) return;

            double groundY = ApparitionTerrain.FindGroundY(capi, spawnX, anchorY, spawnZ);
            Vec3d spawnPos = new Vec3d(spawnX, groundY, spawnZ);

            EntityProperties props = candidates[rand.Next(candidates.Count)];
            string entityCode = props.Code.ToString();
            capi.Logger.Notification($"remedyandruin: Hallucination spawning {entityCode} ({family}) at {spawnPos}");

            IApparitionBehavior apparition = family switch
            {
                ApparitionFamily.Drifter => new DrifterBehavior(capi, entityCode, spawnPos),
                ApparitionFamily.Shiver => new ShiverBehavior(capi, entityCode, spawnPos),
                _ => new BowtornBehavior(capi, entityCode, spawnPos)
            };
            active.Add(apparition);
        }

        private static ApparitionFamily FamilyFromCode(AssetLocation code)
        {
            string path = code?.Path ?? "";
            if (path.StartsWith("shiver-", StringComparison.Ordinal)) return ApparitionFamily.Shiver;
            if (path.StartsWith("bowtorn-", StringComparison.Ordinal)) return ApparitionFamily.Bowtorn;
            return ApparitionFamily.Drifter;
        }

        private List<EntityProperties> GetApparitionCandidates()
        {
            if (apparitionCandidates != null) return apparitionCandidates;

            apparitionCandidates = new List<EntityProperties>();
            foreach (EntityProperties props in capi.World.EntityTypes)
            {
                string path = props.Code?.Path;
                if (path == null) continue;
                foreach (string family in ApparitionFamilies)
                {
                    if (path.StartsWith(family, StringComparison.Ordinal))
                    {
                        apparitionCandidates.Add(props);
                        break;
                    }
                }
            }

            if (apparitionCandidates.Count == 0)
            {
                capi.Logger.Error("remedyandruin: Hallucination found no drifter/bowtorn/shiver entity types in capi.World.EntityTypes at all.");
            }
            else
            {
                capi.Logger.Notification($"remedyandruin: Hallucination resolved {apparitionCandidates.Count} apparition candidates: {string.Join(", ", apparitionCandidates.ConvertAll(p => p.Code.ToString()))}");
            }

            return apparitionCandidates;
        }

        public void Dispose()
        {
            foreach (IApparitionBehavior apparition in active)
            {
                apparition.Dispose();
            }
            active.Clear();
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
