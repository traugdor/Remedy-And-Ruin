using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Vintagestory.GameContent;
using Remedy_And_Ruin.RRBehaviors;

namespace Remedy_And_Ruin
{
    public class Remedy_And_RuinModSystem : ModSystem
    {
        private TemporalVignetteRenderer temporalVignetteRenderer;
        private HallucinationManager hallucinationManager;
        private Harmony harmony;

        private const string HarmonyId = "remedyandruin";

        // Every patch in GameEngineTweaks/HarmonyPatches targets server-authoritative logic
        // (damage, drops, effect application) - none belong on the client. Applying them from
        // StartServerSide (guarded here) means they only ever run in a process that's actually
        // acting as a server (including the integrated singleplayer server), and never from a
        // remote multiplayer client, which never runs this server logic at all.
        private static bool serverPatched;

        public static ConfigServer Config;

        public override void Start(ICoreAPI api)
        {
            Mod.Logger.Notification("Hello from template mod: " + api.Side);

            api.RegisterBlockEntityClass("VialMold", typeof(BlockEntityVialMold));
            api.RegisterBlockEntityClass("VialRack", typeof(BlockEntityVialRack));
            api.RegisterEntityBehaviorClass("remedyandruinEffects", typeof(EntityBehaviorRemedyEffects));
            api.RegisterEntityBehaviorClass("remedyandruinArrowPoisonDelivery", typeof(EntityBehaviorArrowPoisonDelivery));
            api.RegisterEntityBehaviorClass("remedyandruinPlayerConditions", typeof(EntityBehaviorPlayerConditions));
            api.RegisterEntityBehaviorClass("remedyandruinLiverDisease", typeof(EntityBehaviorLiverDisease));
            api.RegisterEntityBehaviorClass("remedyandruinResurrectionSickness", typeof(EntityBehaviorResurrectionSickness));
            api.RegisterEntityBehaviorClass("remedyandruinChestColdFamily", typeof(EntityBehaviorChestColdFamily));
            api.RegisterCollectibleBehaviorClass("RemedyArrowPoisoning", typeof(CollectibleBehaviorArrowPoisoning));
            api.RegisterBlockClass("Remedy_And_Ruin.GameEngineTweaks.BlockVial", typeof(BlockVial));
            api.RegisterBlockClass("Remedy_And_Ruin.GameEngineTweaks.BlockVialRack", typeof(BlockVialRack));

            SetupConfig(api);

            if (api.Side == EnumAppSide.Client)
            {
                GenerateAllClusterTextures(api);
                api.Assets.AddModOrigin("remedyandruin", RemedyPoisonClusterTextures.GetAssetRootPath(api));
            }
        }

        private static void SetupConfig(ICoreAPI api)
        {
            Config = ModConfig.ReadConfig<ConfigServer>(api, ConfigServer.configName);
            api.World.Config.SetBool("RemedyAndRuin.debugMode", Config.debugMode);
            api.World.Config.SetBool("RemedyAndRuin.concussionWobbleEnabled", Config.concussionWobbleEnabled);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("remedyandruin:hello"));

            if (!serverPatched)
            {
                harmony = new Harmony(HarmonyId);
                // Patch each [HarmonyPatch] class individually rather than one PatchAll call, so a
                // single patch that can't resolve its target logs an error and gets skipped
                // instead of aborting every other patch in the mod.
                foreach (Type patchType in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
                    if (patchType.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0) continue;

                    try
                    {
                        harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Mod.Logger.Error($"remedyandruin: failed to apply Harmony patch {patchType.FullName} - {ex}");
                    }
                }
                serverPatched = true;
            }

            ChestColdContagionSystem.Start(api);

            api.Event.PlayerNowPlaying += (IServerPlayer player) =>
            {
                Entity entity = player.Entity;
                if (entity != null && entity.GetBehavior<EntityBehaviorRemedyEffects>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorRemedyEffects(entity));
                }

                if (entity != null && entity.GetBehavior<EntityBehaviorPlayerConditions>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorPlayerConditions(entity));
                }

                if (entity != null && entity.GetBehavior<EntityBehaviorLiverDisease>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorLiverDisease(entity));
                }
                if (entity != null && entity.GetBehavior<EntityBehaviorResurrectionSickness>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorResurrectionSickness(entity));
                }
                if (entity != null && entity.GetBehavior<EntityBehaviorChestColdFamily>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorChestColdFamily(entity));
                }
                entity?.GetBehavior<EntityBehaviorLiverDisease>()?.ResumeAfterLogin((EntityAgent)entity);
                entity?.GetBehavior<EntityBehaviorChestColdFamily>()?.ResumeAfterLogin((EntityAgent)entity);
                entity?.GetBehavior<EntityBehaviorPlayerConditions>()?.ResumeAfterLogin((EntityAgent)entity);

                //EntityBehaviorRemedyEffects RRBehavior = entity.GetBehavior<EntityBehaviorRemedyEffects>();
                if(entity != null && entity.GetBehavior<EntityBehaviorRemedyEffects>() is EntityBehaviorRemedyEffects RRBehavior)
                {
                    RRBehavior.ResetToleranceDecayCheckpoint();
                    RRBehavior.ReconstructActiveEffectsOnLogin();

                    if (Config.allowEffectsToExpireWhenOffline != RRBehavior.lastKnownEffectsAdvanceOffline)
                    {
                        Mod.Logger.Warning($"Config setting 'allowEffectsToExpireWhenOffline' changed. Wiping progress for player: {player.PlayerName}.");
                        RRBehavior.DestroyProgress();
                    }
                }
            };

            api.Event.OnEntitySpawn += (Entity entity) =>
            {
                if (!(entity is EntityPlayer) && entity.GetBehavior<EntityBehaviorArrowPoisonDelivery>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorArrowPoisonDelivery(entity));
                }
            };

            api.Event.OnEntitySpawn += (Entity entity) =>
            {
                if (entity.GetBehavior<EntityBehaviorHealth>() != null && entity.GetBehavior<EntityBehaviorPlayerConditions>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorPlayerConditions(entity));
                }

                if (entity.GetBehavior<EntityBehaviorHealth>() != null && entity.GetBehavior<EntityBehaviorLiverDisease>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorLiverDisease(entity));
                }

                if (entity.GetBehavior<EntityBehaviorHealth>() != null && entity.GetBehavior<EntityBehaviorResurrectionSickness>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorResurrectionSickness(entity));
                }

                if (entity.GetBehavior<EntityBehaviorHealth>() != null && entity.GetBehavior<EntityBehaviorChestColdFamily>() == null)
                {
                    entity.AddBehavior(new EntityBehaviorChestColdFamily(entity));
                }
            };

            api.Event.PlayerDisconnect += (IServerPlayer player) =>
            {
                player.Entity?.GetBehavior<EntityBehaviorRemedyEffects>()?.HandleDisconnect();
            };

            api.Event.GameWorldSave += () =>
            {
                foreach (IPlayer player in api.World.AllOnlinePlayers)
                {
                    player.Entity?.GetBehavior<EntityBehaviorRemedyEffects>()?.HandleGameWorldSaving();
                }
            };

            // Debug: BlockBed gates sleeping on Tiredness > 8f - 8.5 clears that gate with minimal
            // fuss for testing sleep-related mechanics without waiting out real Tiredness accrual.
            api.ChatCommands.Create("rrTired")
                .WithDescription("Debug: set your own Tiredness to 8.5, just above the sleep-gate threshold, so you can sleep immediately.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    EntityBehaviorTiredness tiredness = args.Caller.Entity.GetBehavior<EntityBehaviorTiredness>();
                    if (tiredness == null)
                    {
                        return TextCommandResult.Success("No EntityBehaviorTiredness on this entity.");
                    }
                    tiredness.Tiredness = 8.5f;
                    return TextCommandResult.Success("Tiredness set to 8.5 - you should be able to sleep now.");
                });

            api.ChatCommands.Create("rrSkinIrritation")
                .WithDescription("Debug: toggle Skin Irritation on yourself to verify the debuff/self-resolve/clear cycle.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    EntityBehaviorPlayerConditions conditions = args.Caller.Entity.GetBehavior<EntityBehaviorPlayerConditions>();
                    if (conditions.HasSkinIrritation)
                    {
                        conditions.ClearSkinIrritation((EntityAgent)args.Caller.Entity);
                        return TextCommandResult.Success("Skin Irritation cleared.");
                    }
                    conditions.ApplySkinIrritation((EntityAgent)args.Caller.Entity);
                    return TextCommandResult.Success("Skin Irritation applied - check healingeffectivness via the character screen.");
                });

            api.ChatCommands.Create("rrWoundInfection")
                .WithDescription("Debug: toggle Wound Infection on yourself to verify the DoT/debuff/cure cycle without waiting on Bleeding's own infection-chance rolls.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    EntityBehaviorPlayerConditions conditions = args.Caller.Entity.GetBehavior<EntityBehaviorPlayerConditions>();
                    if (conditions.HasWoundInfection)
                    {
                        conditions.CureWoundInfection((EntityAgent)args.Caller.Entity, concentrated: true);
                        return TextCommandResult.Success("Wound Infection cleared (concentrated cure - no debuff taper).");
                    }
                    conditions.ApplyWoundInfection((EntityAgent)args.Caller.Entity);
                    return TextCommandResult.Success("Wound Infection applied - check healingeffectivness/walkspeed via the character screen.");
                });

            api.ChatCommands.Create("rrStats")
                .WithDescription("Debug: list every modifier contributing to one of your stat categories (default healingeffectivness), with its blended total.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("category"))
                .HandleWith(args =>
                {
                    string category = args.Parsers[0].IsMissing ? "healingeffectivness" : (string)args.Parsers[0].GetValue();
                    EntityStats stats = args.Caller.Entity.Stats;
                    foreach (KeyValuePair<string, EntityFloatStats> entry in stats)
                    {
                        if (entry.Key != category) continue;

                        string lines = string.Join("\n", entry.Value.ValuesByKey.Select(modifier => $"  {modifier.Key} = {modifier.Value.Value:0.###} (weight {modifier.Value.Weight:0.###}, persistent {modifier.Value.Persistent})"));
                        return TextCommandResult.Success($"{category}: blended {stats.GetBlended(category):0.###}\n{lines}");
                    }
                    return TextCommandResult.Success($"No stat category '{category}' on you.");
                });

            api.ChatCommands.Create("rrStatRemove")
                .WithDescription("Debug: remove one modifier key from one of your stat categories, e.g. to clear an orphaned modifier. Use /rrStats first to see the keys.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.Word("category"), api.ChatCommands.Parsers.Word("key"))
                .HandleWith(args =>
                {
                    string category = (string)args.Parsers[0].GetValue();
                    string key = (string)args.Parsers[1].GetValue();
                    args.Caller.Entity.Stats.Remove(category, key);
                    return TextCommandResult.Success($"Removed '{key}' from '{category}'. New blended value: {args.Caller.Entity.Stats.GetBlended(category):0.###}");
                });

            api.ChatCommands.Create("rrResurrectionSickness")
                .WithDescription("Debug: toggle Resurrection Sickness on yourself. Turning it on also drops your Temporal Stability to 50%, as a real resurrection does.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    EntityBehaviorResurrectionSickness sickness = args.Caller.Entity.GetBehavior<EntityBehaviorResurrectionSickness>();
                    EntityAgent agent = (EntityAgent)args.Caller.Entity;
                    if (sickness.HasResurrectionSickness)
                    {
                        sickness.ClearResurrectionSickness(agent);
                        return TextCommandResult.Success("Resurrection Sickness cleared.");
                    }
                    sickness.ApplyResurrectionSickness(agent);
                    EntityBehaviorTemporalStabilityAffected stability = agent.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
                    if (stability != null) stability.OwnStability = 0.5;
                    return TextCommandResult.Success("Resurrection Sickness applied for 1 in-game day, Temporal Stability set to 50%.");
                });

            api.ChatCommands.Create("rrChestCold")
                .WithDescription("Debug: advance your Chest Cold family stage one step (None -> Chest Cold -> Bronchitis -> Flu -> Pneumonia -> None).")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    EntityBehaviorChestColdFamily chestCold = args.Caller.Entity.GetBehavior<EntityBehaviorChestColdFamily>();
                    EntityAgent agent = (EntityAgent)args.Caller.Entity;
                    switch (chestCold.CurrentStage)
                    {
                        case EntityBehaviorChestColdFamily.Stage.None:
                            chestCold.TriggerChestCold(agent);
                            return TextCommandResult.Success("Chest Cold applied.");
                        case EntityBehaviorChestColdFamily.Stage.ChestCold:
                            chestCold.DebugSetStage(agent, EntityBehaviorChestColdFamily.Stage.Bronchitis);
                            return TextCommandResult.Success("Stage set to Bronchitis.");
                        case EntityBehaviorChestColdFamily.Stage.Bronchitis:
                            chestCold.DebugSetStage(agent, EntityBehaviorChestColdFamily.Stage.Flu);
                            return TextCommandResult.Success("Stage set to Flu.");
                        case EntityBehaviorChestColdFamily.Stage.Flu:
                            chestCold.DebugSetStage(agent, EntityBehaviorChestColdFamily.Stage.Pneumonia);
                            return TextCommandResult.Success("Stage set to Pneumonia.");
                        default:
                            chestCold.CureCurrentStage(agent);
                            return TextCommandResult.Success("Chest Cold family illness cured.");
                    }
                });

            api.ChatCommands.Create("rrContagion")
                .WithDescription("Debug: simulate sharing a room with a sick player. Runs the given number of 30-second transmission rolls (default 1) against you at the real chance, skipping the room and distance checks.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalInt("rolls", 1))
                .HandleWith(args =>
                {
                    int rolls = System.Math.Max(1, (int)args[0]);
                    (bool infected, int rollsRun, double chance) = ChestColdContagionSystem.SimulateSickRoommate((EntityPlayer)args.Caller.Entity, rolls);
                    if (rollsRun == 0)
                    {
                        return TextCommandResult.Success("You are already sick; no transmission rolls ran.");
                    }

                    return TextCommandResult.Success(infected
                        ? $"Caught Chest Cold on roll {rollsRun} (chance per roll {chance:P1})."
                        : $"No infection after {rollsRun} roll(s) (chance per roll {chance:P1}).");
                });

            api.ChatCommands.Create("rrLiverDisease")
                .WithDescription("Debug: set your liver toxicity meter (3+ = Liver Disease, 6+ = Liver Failure, 0 clears both). With no value, shows the current meter.")
                .RequiresPrivilege(Privilege.root)
                .RequiresPlayer()
                .WithArgs(api.ChatCommands.Parsers.OptionalFloat("toxicity"))
                .HandleWith(args =>
                {
                    EntityBehaviorLiverDisease liverDisease = args.Caller.Entity.GetBehavior<EntityBehaviorLiverDisease>();
                    if (args.Parsers[0].IsMissing)
                    {
                        return TextCommandResult.Success($"Liver toxicity: {liverDisease.CurrentToxicity:0.00} (Liver Disease: {liverDisease.HasLiverDisease}, Liver Failure: {liverDisease.HasLiverFailure})");
                    }

                    liverDisease.SetToxicity((EntityAgent)args.Caller.Entity, (float)args.Parsers[0].GetValue());
                    return TextCommandResult.Success($"Liver toxicity set to {liverDisease.CurrentToxicity:0.00} (Liver Disease: {liverDisease.HasLiverDisease}, Liver Failure: {liverDisease.HasLiverFailure})");
                });
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("remedyandruin:hello"));

            temporalVignetteRenderer = new TemporalVignetteRenderer(api);
            // Combines the .rrbrainrot debug command's manual sample with Mind Poison's real,
            // server-driven severity so either can independently trigger Hallucination spawns.
            hallucinationManager = new HallucinationManager(api, () => Math.Max(temporalVignetteRenderer.MindPoisonFogStrength, temporalVignetteRenderer.MindPoisonServerSeverity));

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
            serverPatched = false;
            ChestColdContagionSystem.Stop();
            base.Dispose();
        }

    }
}
