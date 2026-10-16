using Newtonsoft.Json;
using System.Text.Json.Serialization;
using Vintagestory.API.Common;

namespace Remedy_And_Ruin
{
    // Same shape as ExpandedStomach's own Config/ConfigServer.cs: a JsonProperty-ordered
    // property per setting, and a (ICoreAPI, previousConfig) constructor that
    // ModConfig.ReadConfig uses to create a fresh file on first run and to carry existing
    // values forward across a config migration.
    public class ConfigServer : IModConfig
    {
        public const string configName = "remedyandruinServer.json";

        [JsonProperty(Order = 1)]
        public string description => "Remedy And Ruin server-side settings.";

        [JsonProperty(Order = 2)]
        public bool debugMode { get; set; } = false;

        [JsonProperty(Order = 3)]
        public bool concussionWobbleEnabled { get; set; } = true;

        [JsonProperty(Order = 4)]
        public string ExpireOfflineDescription => "Changing this after world start will irreversibly reset all progress and effects.";

        [JsonProperty(Order = 5)]
        public bool allowEffectsToExpireWhenOffline {  get; set; } = false;

        [JsonProperty(Order = 6)]
        public float toxicityDecayPerRealSecond { get; set; } = 0.01f;

        [JsonProperty(Order = 7)]
        public float toxicityOverdoseThreshold { get; set; } = 1.0f;

        [JsonProperty(Order = 8)]
        public bool injuryChatMessagesEnabled { get; set; } = true;

        [JsonProperty(Order = 9)]
        public bool allowBowWithUnsplintedBrokenArm { get; set; } = false;

        [JsonProperty(Order = 10)]
        public float brokenArmBowPenaltyPercent { get; set; } = 40f;

        // When true, bush meat from animals killed by Toxic arrow poison drops as discolored bush
        // meat; when false it is removed. Read server-side only.
        [JsonProperty(Order = 11)]
        public bool discoloredBushMeatEnabled { get; set; } = false;

        // World config keys that publish the settings the client needs to read.
        public const string AllowBowWorldKey = "RemedyAndRuin.allowBowWithUnsplintedBrokenArm";
        public const string BowPenaltyWorldKey = "RemedyAndRuin.brokenArmBowPenaltyPercent";

        // Reads the published setting from the world config; the default applies when it is absent.
        public static bool AllowBowWithUnsplintedBrokenArm(IWorldAccessor world) =>
            world?.Config?.GetBool(AllowBowWorldKey, false) ?? false;

        // Bow draw-time and damage penalty percentage for a broken arm, limited to 0 through 90.
        public static float BrokenArmBowPenaltyPercent(IWorldAccessor world)
        {
            float percent = world?.Config?.GetFloat(BowPenaltyWorldKey, 40f) ?? 40f;
            return System.Math.Clamp(percent, 0f, 90f);
        }

        public ConfigServer(ICoreAPI api, ConfigServer previousConfig = null)
        {
            if (previousConfig == null) return;

            debugMode = previousConfig.debugMode;
            concussionWobbleEnabled = previousConfig.concussionWobbleEnabled;
            allowEffectsToExpireWhenOffline = previousConfig.allowEffectsToExpireWhenOffline;
            toxicityDecayPerRealSecond = previousConfig.toxicityDecayPerRealSecond;
            toxicityOverdoseThreshold = previousConfig.toxicityOverdoseThreshold;
            injuryChatMessagesEnabled = previousConfig.injuryChatMessagesEnabled;
            allowBowWithUnsplintedBrokenArm = previousConfig.allowBowWithUnsplintedBrokenArm;
            brokenArmBowPenaltyPercent = previousConfig.brokenArmBowPenaltyPercent;
            discoloredBushMeatEnabled = previousConfig.discoloredBushMeatEnabled;
        }
    }
}
