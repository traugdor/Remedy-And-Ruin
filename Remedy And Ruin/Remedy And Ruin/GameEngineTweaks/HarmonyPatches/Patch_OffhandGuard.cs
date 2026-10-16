using HarmonyLib;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Decides which stacks a player with a broken arm may hold in the offhand slot.
    ///
    /// Free: every stack is allowed. Locked (unsplinted Break or Crush ChestArms): no stack is
    /// allowed except sticks, the splint material, which are allowed in every mode.
    /// </summary>
    public static class OffhandRules
    {
        // Sticks are the splint material that SplintingInteraction takes from the offhand, so they are
        // allowed in every mode; without them an unsplinted broken arm could never be splinted.
        public static bool IsSplintMaterial(ItemStack stack) =>
            stack?.Collectible?.Code != null
            && stack.Collectible.Code.Domain == GlobalConstants.DefaultDomain
            && stack.Collectible.Code.Path == "stick";

        public static bool Allows(OffhandMode mode, ItemStack stack)
        {
            if (IsSplintMaterial(stack)) return true;

            return mode != OffhandMode.Locked;
        }
    }

    internal static class OffhandGuard
    {
        // True when the slot is the offhand slot of a player whose offhand is locked and refuses the source stack.
        public static bool Denies(ItemSlot offhandSlot, ItemSlot sourceSlot)
        {
            if (offhandSlot is not ItemSlotOffhand) return false;
            if (offhandSlot.Inventory is not InventoryBasePlayer inventory) return false;

            IPlayer player = inventory.Player;
            if (player?.Entity == null) return false;

            OffhandMode mode = EntityBehaviorBrokenBones.GetOffhandMode(player.Entity);
            if (mode == OffhandMode.Free) return false;

            return !OffhandRules.Allows(mode, sourceSlot?.Itemstack);
        }
    }

    /// <summary>
    /// Shared patch set. Refuses to hold a stack in the offhand slot while the owner's broken arm
    /// forbids it. ItemSlot.CanHold is used by click, drag and hotbar-swap placement and by
    /// TryFlipWith.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.CanHold))]
    public static class Patch_OffhandCanHold
    {
        public static void Postfix(ItemSlot __instance, ItemSlot sourceSlot, ref bool __result)
        {
            if (__result && OffhandGuard.Denies(__instance, sourceSlot)) __result = false;
        }
    }

    /// <summary>
    /// Shared patch set. Refuses to merge or move a stack into the offhand slot while the owner's
    /// broken arm forbids it. ItemSlot.CanTakeFrom is used by TryPutInto, shift-click transfers and
    /// the best-slot search behind item auto-pickup, so a refused stack goes to another slot or stays
    /// on the ground.
    /// </summary>
    [SharedPatch]
    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.CanTakeFrom))]
    public static class Patch_OffhandCanTakeFrom
    {
        public static void Postfix(ItemSlot __instance, ItemSlot sourceSlot, ref bool __result)
        {
            if (__result && OffhandGuard.Denies(__instance, sourceSlot)) __result = false;
        }
    }
}
