using HarmonyLib;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Holds a player in a bed while their Sedative dose is overdosed. Every dismount from a bed
    /// (sneak key, Tiredness reaching 0 in RestPlayer, a temporal storm, a broken bed, death, a
    /// teleport) goes through EntityAgent.TryUnmount, which asks IMountableSeat.CanUnmount first, so
    /// this vetoes that one call; other mountables have their own CanUnmount and are untouched.
    ///
    /// The hold applies to any bed sleep while the overdosed dose record is unexpired, wherever the
    /// player was when the overdose began. Nothing needs to release the player: the record expires
    /// at the end of the dose window, and RestPlayer retries its Tiredness-0 unmount every tick, so
    /// the first tick after expiry wakes them.
    ///
    /// Shared set: the server decides, and the client vetoes its local sneak-key dismount so it does
    /// not drift from the server. The client holds only while its own "mountedOn" attribute still
    /// lists the bed, because the client calls TryUnmount itself once the server has removed that
    /// attribute (death, broken bed, storm) and that call must succeed.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(BlockEntityBed), nameof(BlockEntityBed.CanUnmount))]
    public static class Patch_SedativeBedHold
    {
        public static bool Prefix(EntityAgent entityAgent, bool ___blockBroken, ref bool __result)
        {
            if (!(entityAgent is EntityPlayer player) || !player.Alive) return true;
            if (!PotionEffects.IsOverdosed(player, EntityBehaviorRemedyEffects.EffectCluster.SEDATIVE)) return true;

            if (player.World.Side == EnumAppSide.Client)
            {
                if (!player.WatchedAttributes.HasAttribute("mountedOn")) return true;
            }
            else
            {
                if (___blockBroken) return true;
                if (IsTemporalStormWakingSleepers(player)) return true;
            }

            __result = false;
            return false;
        }

        // Mirrors the condition under which vanilla's RestPlayer ends a sleep for a storm.
        private static bool IsTemporalStormWakingSleepers(EntityPlayer player)
        {
            if (player.World.Config.GetString("temporalStormSleeping", "0").ToInt() != 0) return false;
            return player.Api.ModLoader.GetModSystem<SystemTemporalStability>().StormStrength > 0f;
        }
    }
}
