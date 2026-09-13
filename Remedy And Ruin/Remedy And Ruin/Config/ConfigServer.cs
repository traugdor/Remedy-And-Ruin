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

        public ConfigServer(ICoreAPI api, ConfigServer previousConfig = null)
        {
            if (previousConfig == null) return;

            debugMode = previousConfig.debugMode;
            concussionWobbleEnabled = previousConfig.concussionWobbleEnabled;
            allowEffectsToExpireWhenOffline = previousConfig.allowEffectsToExpireWhenOffline;
        }
    }
}
