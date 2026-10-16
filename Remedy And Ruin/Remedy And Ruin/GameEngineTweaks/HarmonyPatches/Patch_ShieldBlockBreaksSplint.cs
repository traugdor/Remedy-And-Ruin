using HarmonyLib;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Server patch set. ModSystemWearableStats.applyShieldProtection reduces the damage when a held
    /// shield absorbs part of a hit, so a returned damage below the incoming damage means a block.
    /// The method is also reached through the client's own damage prediction; the postfix acts on the
    /// server world only.
    ///
    /// The vanilla method walks the offhand slot and then the main-hand slot and damages every shield
    /// whose angle covers the hit, whether or not it absorbed anything. The prefix records each
    /// shield's durability and the postfix prefers a shield whose durability dropped, then falls back
    /// to the offhand and main-hand shield in that order.
    /// </summary>
    [HarmonyPatch(typeof(ModSystemWearableStats), "applyShieldProtection")]
    public static class Patch_ShieldBlockBreaksSplint
    {
        // The vanilla method reassigns its own damage parameter, and a postfix reads parameters as they
        // stand after the method ran, so the incoming damage has to be captured here.
        public sealed class ShieldState
        {
            public float IncomingDamage;
            public int[] Durabilities;
        }

        public static void Prefix(IPlayer player, float damage, out ShieldState __state)
        {
            __state = new ShieldState
            {
                IncomingDamage = damage,
                Durabilities = new[]
                {
                    Durability(player?.Entity?.LeftHandItemSlot),
                    Durability(player?.Entity?.RightHandItemSlot)
                }
            };
        }

        public static void Postfix(IPlayer player, float __result, ShieldState __state)
        {
            if (__result >= __state.IncomingDamage) return;

            EntityPlayer entity = player?.Entity;
            if (entity == null || entity.World.Side != EnumAppSide.Server) return;

            EntityBehaviorBrokenBones bones = entity.GetBehavior<EntityBehaviorBrokenBones>();
            if (bones == null) return;

            ItemSlot[] slots = { entity.LeftHandItemSlot, entity.RightHandItemSlot };
            ItemSlot blocker = null;
            for (int i = 0; i < slots.Length && blocker == null; i++)
            {
                if (IsShield(slots[i]) && Durability(slots[i]) < __state.Durabilities[i]) blocker = slots[i];
            }
            for (int i = 0; i < slots.Length && blocker == null; i++)
            {
                if (IsShield(slots[i])) blocker = slots[i];
            }

            if (blocker != null) bones.OnShieldBlock(blocker);
        }

        internal static bool IsShield(ItemSlot slot)
        {
            CollectibleObject collectible = slot?.Itemstack?.Collectible;
            if (collectible == null) return false;

            return collectible is ItemShield
                || collectible.Tool == EnumTool.Shield
                || slot.Itemstack.ItemAttributes?["shield"]?.Exists == true;
        }

        private static int Durability(ItemSlot slot) =>
            slot?.Itemstack == null ? -1 : slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack);
    }
}
