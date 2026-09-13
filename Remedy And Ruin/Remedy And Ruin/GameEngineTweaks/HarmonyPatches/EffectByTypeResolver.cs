using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Resolves a collectible's "remedyandruinEffectByType" attribute - a wildcard-pattern-keyed
    /// dictionary (e.g. "*-deathcap-*") - against that collectible's own code, returning the
    /// matched entry's data already shaped like a flat "remedyandruinEffect" attribute
    /// (cluster/effectMultiplier/onsetMultiplier/etc.), or null if there's no match.
    /// </summary>
    internal static class EffectByTypeResolver
    {
        public static JsonObject Resolve(CollectibleObject collectible)
        {
            JsonObject byType = collectible?.Attributes?["remedyandruinEffectByType"];
            string codePath = collectible?.Code?.Path;
            if (byType == null || !byType.Exists || codePath == null) return null;
            if (!(byType.Token is JObject jobj)) return null;

            foreach (JProperty prop in jobj.Properties())
            {
                if (WildcardUtil.Match(prop.Name, codePath))
                {
                    return new JsonObject(prop.Value);
                }
            }
            return null;
        }
    }
}
