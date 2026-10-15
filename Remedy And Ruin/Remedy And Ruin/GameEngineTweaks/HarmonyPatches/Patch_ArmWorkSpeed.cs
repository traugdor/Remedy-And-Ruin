using HarmonyLib;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Shared patch set. Block-break speed is computed on the client (Block.OnGettingBroken runs only
    /// there, every 40 ms while breaking), so the prefix scales the elapsed-time argument by the local
    /// player's ChestArms work-speed factor, read from the synced WatchedAttributes.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(Block), nameof(Block.OnGettingBroken))]
    public static class Patch_ArmDigSpeed
    {
        public static void Prefix(IPlayer player, ref float dt)
        {
            ArmDigSpeed.Scale(player, ref dt);
        }
    }

    /// <summary>
    /// Shared patch set. BlockReeds overrides OnGettingBroken without calling the base method, so it
    /// needs the same scaling as <see cref="Patch_ArmDigSpeed"/>.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(BlockReeds), nameof(BlockReeds.OnGettingBroken))]
    public static class Patch_ArmDigSpeedReeds
    {
        public static void Prefix(IPlayer player, ref float dt)
        {
            ArmDigSpeed.Scale(player, ref dt);
        }
    }

    internal static class ArmDigSpeed
    {
        public static void Scale(IPlayer player, ref float dt)
        {
            if (player?.Entity == null || player.Entity.World.Side != EnumAppSide.Client) return;

            dt *= EntityBehaviorBrokenBones.WorkSpeedMultiplier(player.Entity);
        }
    }

    /// <summary>
    /// Shared patch set. Blocks the start of a bow draw when <see cref="EntityBehaviorBrokenBones.IsBowDrawBlocked"/>.
    /// Leaving the hand handling at NotHandled makes the client skip the start packet, as with no arrow
    /// in the inventory; the server runs the same check as a backstop. The client shows a short error
    /// once per attempt.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(ItemBow), nameof(ItemBow.OnHeldInteractStart))]
    public static class Patch_BowDrawBlocked
    {
        public static bool Prefix(EntityAgent byEntity, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!EntityBehaviorBrokenBones.IsBowDrawBlocked(byEntity)) return true;

            handling = EnumHandHandling.NotHandled;
            if (firstEvent && byEntity.World.Side == EnumAppSide.Client && byEntity.World.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(byEntity, "remedyandruinBowBlocked", Lang.Get("remedyandruin:bones-bow-blocked"));
            }
            return false;
        }
    }

    /// <summary>
    /// Shared patch set. Lengthens the bow draw by dividing the elapsed draw time by the broken-arm
    /// penalty divisor, so the vanilla 0.65 s minimum draw becomes 0.65 s times the divisor. A draw that
    /// is blocked (injured mid-draw) is released with zero time, which runs the vanilla cleanup and
    /// fires nothing. The client's own call returns before using the time.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(ItemBow), nameof(ItemBow.OnHeldInteractStop))]
    public static class Patch_BowDrawTime
    {
        public static void Prefix(EntityAgent byEntity, ref float secondsUsed)
        {
            if (EntityBehaviorBrokenBones.IsBowDrawBlocked(byEntity))
            {
                secondsUsed = 0f;
                return;
            }

            secondsUsed /= EntityBehaviorBrokenBones.BowDrawPenaltyDivisor(byEntity);
        }
    }

    /// <summary>
    /// Server set. Reduces arrow damage by the broken-arm penalty when a player fires a bow. Reads the
    /// shooter's synced WatchedAttributes; thrown weapons are unaffected because WeaponStack must be a bow.
    /// </summary>
    [HarmonyPatch(typeof(EntityProjectileBase), "ApplyModifiers")]
    public static class Patch_BowArmDamage
    {
        public static void Postfix(EntityProjectileBase __instance, ref float damage)
        {
            if (__instance.FiredBy is not EntityPlayer) return;
            if (__instance.WeaponStack?.Collectible is not ItemBow) return;

            damage *= EntityBehaviorBrokenBones.BowDamageMultiplier(__instance.FiredBy);
        }
    }
}
