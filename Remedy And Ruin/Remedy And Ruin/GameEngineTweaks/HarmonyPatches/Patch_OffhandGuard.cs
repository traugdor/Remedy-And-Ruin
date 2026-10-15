using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Decides which stacks a player with a broken arm may hold in the offhand slot.
    ///
    /// Sticks (the splint material) are allowed in every mode and cannot be blocked by configuration.
    /// Locked (unsplinted Break or Crush): no other stack is allowed. Restricted (splinted Break or
    /// Crush): a stack is refused when <see cref="IsBlockedWhenSplinted"/> or an offhandExtraBlockedItems
    /// pattern matches it, unless an offhandAlwaysAllowedItems pattern matches it first. The
    /// always-allowed list does not lift the Locked mode.
    ///
    /// Config patterns are item-code wildcards matched with WildcardUtil against the collectible's
    /// Code. An entry with a domain ("somemod:*shield*") matches that domain only (a "*" domain
    /// matches any); an entry without a domain matches the path in every domain. Both lists are read
    /// from the world config, so the client sees the server's values, and are parsed once per
    /// distinct setting value.
    ///
    /// The built-in verdict is cached per collectible, so it is computed once per item type and side.
    /// </summary>
    public static class OffhandRules
    {
        private static readonly string[] BlockedTags = { "tool-hammer", "tool-wrench" };
        private static readonly string[] BlockedCodes = { "tongsmetal", "punchset" };

        private sealed class Box<T>
        {
            public T Value;
        }

        private sealed class PatternList
        {
            public string Raw = "";
            public AssetLocation[] Items = Array.Empty<AssetLocation>();
        }

        private static readonly ConditionalWeakTable<CollectibleObject, Box<bool>> BuiltInVerdicts = new();
        private static readonly ConditionalWeakTable<object, Box<TagSet>> TagSets = new();
        private static PatternList extraBlocked = new();
        private static PatternList alwaysAllowed = new();

        // Sticks are the splint material that SplintingInteraction takes from the offhand, so they are
        // allowed in every mode; without them an unsplinted broken arm could never be splinted.
        public static bool IsSplintMaterial(ItemStack stack) =>
            stack?.Collectible?.Code != null
            && stack.Collectible.Code.Domain == GlobalConstants.DefaultDomain
            && stack.Collectible.Code.Path == "stick";

        public static bool Allows(OffhandMode mode, ItemStack stack, IWorldAccessor world)
        {
            if (IsSplintMaterial(stack)) return true;

            switch (mode)
            {
                case OffhandMode.Locked:
                    return false;
                case OffhandMode.Restricted:
                    if (stack?.Collectible == null) return true;
                    if (Matches(ref alwaysAllowed, ConfigServer.OffhandAlwaysAllowedItems(world), stack.Collectible.Code)) return true;
                    if (IsBlockedWhenSplinted(stack, world)) return false;
                    return !Matches(ref extraBlocked, ConfigServer.OffhandExtraBlockedItems(world), stack.Collectible.Code);
                default:
                    return true;
            }
        }

        // The built-in blocked set: shields, hammers, wrenches, oars, metal tongs and punch sets.
        // Wooden tongs share the tool-tongs tag with metal tongs, so tongs are matched by code only.
        public static bool IsBlockedWhenSplinted(ItemStack stack, IWorldAccessor world)
        {
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null) return false;

            if (BuiltInVerdicts.TryGetValue(collectible, out Box<bool> cached)) return cached.Value;

            bool blocked = ComputeBlocked(stack, collectible, world);
            BuiltInVerdicts.AddOrUpdate(collectible, new Box<bool> { Value = blocked });
            return blocked;
        }

        private static bool ComputeBlocked(ItemStack stack, CollectibleObject collectible, IWorldAccessor world)
        {
            if (collectible is ItemShield || collectible is ItemOar) return true;
            if (collectible.Attributes?["shield"]?.Exists == true) return true;

            EnumTool? tool = collectible.Tool;
            if (tool == EnumTool.Shield || tool == EnumTool.Hammer || tool == EnumTool.Wrench) return true;

            AssetLocation code = collectible.Code;
            if (code != null && code.Domain == "game")
            {
                foreach (string blockedCode in BlockedCodes)
                {
                    if (code.Path == blockedCode || code.Path.StartsWith(blockedCode + "-", StringComparison.Ordinal)) return true;
                }
            }

            return HasBlockedTag(stack, collectible, world);
        }

        // Tags are registered per API instance, so the blocked tag set is built once per registry.
        private static bool HasBlockedTag(ItemStack stack, CollectibleObject collectible, IWorldAccessor world)
        {
            ITagRegistry<TagSet> registry = world?.Api?.CollectibleTagRegistry;
            if (registry == null) return false;

            if (!TagSets.TryGetValue(registry, out Box<TagSet> blockedTags))
            {
                registry.TryCreateTagSet(out TagSet set, (IEnumerable<string>)BlockedTags);
                blockedTags = new Box<TagSet> { Value = set };
                TagSets.AddOrUpdate(registry, blockedTags);
            }

            TagSet itemTags = collectible.GetTags(stack);
            return itemTags.Overlaps(blockedTags.Value);
        }

        private static bool Matches(ref PatternList cache, string raw, AssetLocation code)
        {
            if (string.IsNullOrEmpty(raw) || code == null) return false;

            PatternList list = cache;
            if (!string.Equals(list.Raw, raw, StringComparison.Ordinal))
            {
                list = Parse(raw);
                cache = list;
            }

            foreach (AssetLocation pattern in list.Items)
            {
                if (WildcardUtil.Match(pattern, code)) return true;
            }
            return false;
        }

        private static PatternList Parse(string raw)
        {
            List<AssetLocation> items = new();
            foreach (string entry in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int colon = entry.IndexOf(':');
                items.Add(colon < 0
                    ? new AssetLocation("*", entry)
                    : new AssetLocation(entry.Substring(0, colon), entry.Substring(colon + 1)));
            }
            return new PatternList { Raw = raw, Items = items.ToArray() };
        }
    }

    internal static class OffhandGuard
    {
        // True when the slot is an offhand slot of a restricted player and refuses the source stack.
        public static bool Denies(ItemSlot offhandSlot, ItemSlot sourceSlot)
        {
            if (offhandSlot is not ItemSlotOffhand) return false;
            if (offhandSlot.Inventory is not InventoryBasePlayer inventory) return false;

            IPlayer player = inventory.Player;
            if (player?.Entity == null) return false;

            OffhandMode mode = EntityBehaviorBrokenBones.GetOffhandMode(player.Entity);
            if (mode == OffhandMode.Free) return false;

            return !OffhandRules.Allows(mode, sourceSlot?.Itemstack, player.Entity.World);
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
