using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Remedy_And_Ruin.GameEngineTweaks;
using Remedy_And_Ruin.GameEngineTweaks.Hallucination;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Remedy_And_Ruin
{
    public class Remedy_And_RuinModSystem : ModSystem
    {
        private TemporalVignetteRenderer temporalVignetteRenderer;
        private HallucinationManager hallucinationManager;
        private Harmony harmony;

        private const string HarmonyId = "remedyandruin";

        public static ConfigServer Config;

        // Runs on both server and client: registers the VialMold block entity class,
        // loads/creates the config, applies Harmony patches (eating pipeline overrides), and
        // (client only) generates cluster textures.
        public override void Start(ICoreAPI api)
        {
            Mod.Logger.Notification("Hello from template mod: " + api.Side);

            api.RegisterBlockEntityClass("VialMold", typeof(BlockEntityVialMold));
            api.RegisterEntityBehaviorClass("remedyandruinEffects", typeof(EntityBehaviorRemedyEffects));

            SetupConfig(api);

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            if (api.Side == EnumAppSide.Client)
            {
                GenerateAllClusterTextures(api);
                api.Assets.AddModOrigin("remedyandruin", RemedyPoisonClusterTextures.GetAssetRootPath(api));
            }
        }

        // Same load/regenerate pattern as ExpandedStomach's own setupConfig - reads (or
        // creates, on first run) remedyandruinServer.json via ModConfig, then mirrors each
        // setting onto api.World.Config so both sides can read it without re-parsing the file.
        private static void SetupConfig(ICoreAPI api)
        {
            Config = ModConfig.ReadConfig<ConfigServer>(api, ConfigServer.configName);
            api.World.Config.SetBool("RemedyAndRuin.debugMode", Config.debugMode);
            api.World.Config.SetBool("RemedyAndRuin.concussionWobbleEnabled", Config.concussionWobbleEnabled);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("remedyandruin:hello"));

            api.Event.PlayerNowPlaying += (IServerPlayer player) =>
            {
                Entity entity = player.Entity;
                if (entity != null && entity.GetBehavior<EntityBehaviorRemedyEffects>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorRemedyEffects(entity));
                }

                //EntityBehaviorRemedyEffects RRBehavior = entity.GetBehavior<EntityBehaviorRemedyEffects>();
                if(entity != null && entity.GetBehavior<EntityBehaviorRemedyEffects>() is EntityBehaviorRemedyEffects RRBehavior)
                {
                    if (Config.allowEffectsToExpireWhenOffline != RRBehavior.lastKnownEffectsAdvanceOffline)
                    {
                        Mod.Logger.Warning($"Config setting 'allowEffectsToExpireWhenOffline' changed. Wiping progress for player: {player.PlayerName}.");
                        RRBehavior.DestroyProgress();
                    }
                }
            };
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("remedyandruin:hello"));

            temporalVignetteRenderer = new TemporalVignetteRenderer(api);
            hallucinationManager = new HallucinationManager(api, () => temporalVignetteRenderer.MindPoisonFogStrength);

            api.ChatCommands.Create("rrTempFog")
                .WithDescription("Debug: set Temporal Fog's sepia/desaturation strength (0-1) for visual tuning.")
                .WithArgs(api.ChatCommands.Parsers.OptionalFloat("strength"))
                .HandleWith(args =>
                {
                    if (args.Parsers[0].IsMissing)
                    {
                        return TextCommandResult.Success($"Current Temporal Fog strength: {temporalVignetteRenderer.TempFogStrength}");
                    }

                    float value = (float)args.Parsers[0].GetValue();
                    temporalVignetteRenderer.TempFogStrength = GameMath.Clamp(value, 0f, 1f);
                    return TextCommandResult.Success($"Temporal Fog strength set to {temporalVignetteRenderer.TempFogStrength}");
                });

            api.ChatCommands.Create("rrConcussion")
                .WithDescription("Debug: set Skull-Strain's concussion strength (0-1) for visual tuning - horizontal shear, contrast/brightness boost, and drunken sway.")
                .WithArgs(api.ChatCommands.Parsers.OptionalFloat("strength"))
                .HandleWith(args =>
                {
                    if (args.Parsers[0].IsMissing)
                    {
                        return TextCommandResult.Success($"Current concussion strength: {temporalVignetteRenderer.ConcussionStrength}");
                    }

                    float value = (float)args.Parsers[0].GetValue();
                    temporalVignetteRenderer.ConcussionStrength = GameMath.Clamp(value, 0f, 1f);
                    return TextCommandResult.Success($"Concussion strength set to {temporalVignetteRenderer.ConcussionStrength}");
                });

            api.ChatCommands.Create("rrbrainrot")
                .WithDescription("Debug: sample Mind Poison's combined effect (Temporal Fog, drunken wobble, psychedelic trip) at a given strength (0-1) for visual tuning.")
                .WithArgs(api.ChatCommands.Parsers.OptionalFloat("strength"))
                .HandleWith(args =>
                {
                    if (args.Parsers[0].IsMissing)
                    {
                        return TextCommandResult.Success($"Current Brain Rot strength: {temporalVignetteRenderer.MindPoisonFogStrength}");
                    }

                    float value = GameMath.Clamp((float)args.Parsers[0].GetValue(), 0f, 1f);
                    EntityPlayer plr = api.World.Player.Entity;
                    // MindPoisonFogStrength, not TempFogStrength - that's the real Temporal Fog
                    // condition's own field; writing it here would let Brain Rot and Temporal
                    // Fog overwrite each other's value instead of coexisting independently.
                    temporalVignetteRenderer.MindPoisonFogStrength = value;
                    // DrunkWobbleStrength, not ConcussionStrength - that also draws the
                    // shear/contrast-boost vignette, which isn't wanted here.
                    temporalVignetteRenderer.DrunkWobbleStrength = value;
                    plr.WatchedAttributes.SetFloat("psychedelic", value * 2f);
                    return TextCommandResult.Success($"Brain Rot strength set to {value}");
                });

            api.ChatCommands.Create("rrConfig")
                .WithDescription("Remedy And Ruin config. Use `/help rrConfig` for more information.")
                .BeginSubCommand("cWobble")
                    .WithDescription("Accessibility: drunken camera sway from any source (Skull-Strain concussion, Mind Poison). Persisted to remedyandruinServer.json. Leaves the screen color grade and horizontal shear untouched either way. Omit the value to toggle the current setting.")
                    .WithArgs(api.ChatCommands.Parsers.OptionalBool("enabled"))
                    .HandleWith(args =>
                    {
                        bool value = args.Parsers[0].IsMissing
                            ? !Config.concussionWobbleEnabled
                            : (bool)args.Parsers[0].GetValue();
                        Config.concussionWobbleEnabled = value;
                        ModConfig.WriteConfig(api, ConfigServer.configName, Config);
                        return TextCommandResult.Success($"Concussion drunken sway {(value ? "enabled" : "disabled")}.");
                    })
                .EndSubCommand();
        }

        /// <summary>
        /// Generates and registers every remedy/poison cluster's liquid texture (from
        /// waterportion) and Base texture (from rot) at mod startup - no debug command
        /// needed, this just happens as part of loading the mod. Cheap after the first run per
        /// cluster, since EnsureGeneratedTexture skips regeneration once the file already
        /// exists on disk. Runs from Start(), not AssetsLoaded(): the engine's one-time asset
        /// origin scan (AssetManager.AddExternalAssets) happens after every mod's Start() but
        /// before any mod's AssetsLoaded(), so both the files and the AddModOrigin registration
        /// need to exist by the end of Start() to be seen by that scan - see
        /// RemedyPoisonClusterTextures.GetAssetRootPath for the full timing note. Reads the
        /// source textures straight off disk (BitmapExternal(filePath)) rather than through
        /// ICoreClientAPI.Assets, since the asset system itself isn't safe to read from this
        /// early in the mod lifecycle.
        /// </summary>
        private void GenerateAllClusterTextures(ICoreAPI api)
        {
            string survivalTextures = Path.Combine(GamePaths.AssetsPath, "survival", "textures");
            string liquidSourcePath = Path.Combine(survivalTextures, "block", "liquid", "waterportion.png");
            string containerSourcePath = Path.Combine(survivalTextures, "block", "creature", "rot", "rot.png");
            string solidsSourcePath = Path.Combine(survivalTextures, "item", "resource", "rot", "rot-solids.png");
            string spillSourcePath = Path.Combine(survivalTextures, "item", "resource", "rot", "rot-spill.png");

            using BitmapRef liquidBase = LoadBaseTextureFromDisk(liquidSourcePath, out string liquidError);
            using BitmapRef containerBase = LoadBaseTextureFromDisk(containerSourcePath, out string containerError);
            using BitmapRef solidsBase = LoadBaseTextureFromDisk(solidsSourcePath, out string solidsError);
            using BitmapRef spillBase = LoadBaseTextureFromDisk(spillSourcePath, out string spillError);

            LogIfMissing("liquid", liquidBase, liquidError);
            LogIfMissing("Potion Base container", containerBase, containerError);
            LogIfMissing("Potion Base shape (solids)", solidsBase, solidsError);
            LogIfMissing("Potion Base shape (spill)", spillBase, spillError);

            foreach (RemedyPoisonCluster cluster in Enum.GetValues(typeof(RemedyPoisonCluster)))
            {
                if (liquidBase != null)
                {
                    RemedyPoisonClusterTextures.EnsureGeneratedTexture(api, cluster, liquidBase, RemedyTextureRole.Liquid);
                }
                if (containerBase != null)
                {
                    RemedyPoisonClusterTextures.EnsureGeneratedTexture(api, cluster, containerBase, RemedyTextureRole.PotionBaseContainer);
                }
                if (solidsBase != null)
                {
                    RemedyPoisonClusterTextures.EnsureGeneratedTexture(api, cluster, solidsBase, RemedyTextureRole.PotionBaseShapeSolids);
                }
                if (spillBase != null)
                {
                    RemedyPoisonClusterTextures.EnsureGeneratedTexture(api, cluster, spillBase, RemedyTextureRole.PotionBaseShapeSpill);
                }
            }

            Mod.Logger.Notification($"remedyandruin: generated/registered textures for {Enum.GetValues(typeof(RemedyPoisonCluster)).Length} clusters.");
        }

        private void LogIfMissing(string what, BitmapRef bmp, string error)
        {
            if (bmp == null)
            {
                Mod.Logger.Error($"remedyandruin: could not generate cluster {what} textures - {error}");
            }
        }

        /// <summary>
        /// Loads a vanilla texture straight off disk by its known on-disk path, bypassing
        /// ICoreAPI.Assets entirely - safe to call from Start(), unlike the asset system, which
        /// throws if read from before AssetsLoaded.
        /// </summary>
        private static BitmapRef LoadBaseTextureFromDisk(string absolutePath, out string errorMessage)
        {
            errorMessage = null;
            if (!File.Exists(absolutePath))
            {
                errorMessage = $"Could not find base texture file at {absolutePath}.";
                return null;
            }
            return new BitmapExternal(absolutePath);
        }

        public override void Dispose()
        {
            temporalVignetteRenderer?.Dispose();
            hallucinationManager?.Dispose();
            harmony?.UnpatchAll(HarmonyId);
            base.Dispose();
        }

    }
}
