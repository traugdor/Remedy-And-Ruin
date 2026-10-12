using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Free-standing rack that displays Vials. Right-click on a slot's selection box puts the
    /// held Vial into an empty slot, or takes the Vial out with an empty hand; all slot logic
    /// lives in BlockEntityVialRack.
    /// </summary>
    public class BlockVialRack : Block
    {
        private WorldInteraction[] interactions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            PlacedPriorityInteract = true;

            interactions = ObjectCacheUtil.GetOrCreate(api, "vialrackInteractions", () =>
            {
                ItemStack[] vialStacks = api.World.Collectibles
                    .Where(c => c is BlockVial)
                    .Select(c => new ItemStack(c))
                    .ToArray();

                return new WorldInteraction[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "remedyandruin:blockhelp-vialrack-place",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = vialStacks,
                        GetMatchingStacks = (wi, bs, es) =>
                            api.World.BlockAccessor.GetBlockEntity(bs.Position) is BlockEntityVialRack rack && rack.IsSlotEmpty(bs.SelectionBoxIndex)
                                ? vialStacks
                                : null
                    },
                    new WorldInteraction
                    {
                        ActionLangCode = "remedyandruin:blockhelp-vialrack-take",
                        MouseButton = EnumMouseButton.Right,
                        RequireFreeHand = true,
                        ShouldApply = (wi, bs, es) =>
                            api.World.BlockAccessor.GetBlockEntity(bs.Position) is BlockEntityVialRack rack
                            && bs.SelectionBoxIndex >= 0
                            && bs.SelectionBoxIndex < BlockEntityVialRack.SlotCount
                            && !rack.IsSlotEmpty(bs.SelectionBoxIndex)
                    }
                };
            });
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityVialRack rack)
            {
                return rack.OnInteract(byPlayer, blockSel);
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            WorldInteraction[] baseHelp = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            if (world.Claims.TestAccess(forPlayer, selection.Position, EnumBlockAccessFlags.Use) == EnumWorldAccessResponse.Granted)
            {
                return baseHelp.Concat(interactions).ToArray();
            }
            return baseHelp;
        }
    }
}
