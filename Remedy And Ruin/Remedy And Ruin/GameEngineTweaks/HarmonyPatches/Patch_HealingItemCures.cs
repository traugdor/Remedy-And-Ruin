using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Any completed bandage/poultice application stops Bleeding universally
    /// (EntityBehaviorPlayerConditions.StaunchBleeding), regardless of which item was used. A
    /// bandage/poultice made with Antiseptic (or Concentrated Antiseptic) additionally cures Wound
    /// Infection - identified by the item's code containing "antiseptic", matching this plan's own
    /// bandage-antiseptic/poultice-*-antiseptic variants.
    ///
    /// OnHeldInteractStop only actually heals and consumes the item (slot.TakeOut(1)) once
    /// secondsUsed reaches the item's own application time - a protected, non-static check this
    /// Postfix has no direct way to call. The Prefix instead records the slot's pre-call item code
    /// and stack size; the Postfix treats a shrunk (or emptied) stack as proof the application
    /// completed and no-ops on a cancelled/interrupted one. This also sidesteps TakeOut(1) nulling
    /// out slot.Itemstack outright for a single-item stack, which would otherwise make the item
    /// code unreadable by the time a Postfix runs.
    ///
    /// Target selection mirrors the base game's own GetTargetEntity exactly (protected, so its
    /// logic is reproduced here rather than called): entitySel.Entity is only the target when the
    /// user holds Ctrl, isn't moving, and the target is actually healable - otherwise the item
    /// always applies to the user themself, same as vanilla healing does. Approximating this by
    /// just using entitySel.Entity whenever present (ignoring Ctrl) would silently cure whatever
    /// entity happened to be under the crosshair instead of the user.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleBehaviorHealingItem), "OnHeldInteractStop")]
    public static class Patch_HealingItemCures
    {
        public struct PreApplicationState
        {
            public string ItemCode;
            public int StackSize;
        }

        public static void Prefix(ItemSlot slot, out PreApplicationState __state)
        {
            ItemStack stack = slot?.Itemstack;
            __state = new PreApplicationState
            {
                ItemCode = stack?.Collectible?.Code?.Path,
                StackSize = stack?.StackSize ?? 0
            };
        }

        public static void Postfix(CollectibleBehaviorHealingItem __instance, PreApplicationState __state, ItemSlot slot, EntityAgent byEntity, EntitySelection entitySel)
        {
            if (byEntity.World.Side != EnumAppSide.Server) return;
            if (__state.ItemCode == null) return;

            int newStackSize = slot?.Itemstack?.StackSize ?? 0;
            if (newStackSize >= __state.StackSize) return; // application was cancelled/interrupted - nothing was consumed

            Entity target = ResolveTargetEntity(__instance, slot, byEntity, entitySel);
            EntityBehaviorPlayerConditions conditions = target.GetBehavior<EntityBehaviorPlayerConditions>();
            conditions?.StaunchBleeding();

            if (__state.ItemCode.Contains("antiseptic") && conditions != null)
            {
                bool concentrated = __state.ItemCode.Contains("concentrated"); // matches this mod's own naming for Concentrated-tier items elsewhere
                conditions.CureWoundInfection((EntityAgent)target, concentrated);
            }
        }

        private static Entity ResolveTargetEntity(CollectibleBehaviorHealingItem instance, ItemSlot slot, EntityAgent byEntity, EntitySelection entitySel)
        {
            Entity selectedEntity = entitySel?.Entity;
            if (selectedEntity == null) return byEntity;

            EntityBehaviorHealth healthBehavior = selectedEntity.GetBehavior<EntityBehaviorHealth>();
            bool ctrlHeldStill = byEntity.Controls.CtrlKey && !byEntity.Controls.Forward && !byEntity.Controls.Backward
                && !byEntity.Controls.Left && !byEntity.Controls.Right;

            if (ctrlHeldStill && instance.CanHeal(selectedEntity) && healthBehavior != null && healthBehavior.IsHealable(byEntity, slot))
            {
                return selectedEntity;
            }
            return byEntity;
        }
    }
}
