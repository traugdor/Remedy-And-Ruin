using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Eight-slot display for Vials: slots 0-3 are the bottom shelf and slots 4-7 the top shelf,
    /// each ordered along +x of the unrotated model. Slot i corresponds to selection box i in
    /// vialrack.json, so BlockSelection.SelectionBoxIndex is the slot index (indices past the
    /// last slot are frame boxes). Each slot holds one Vial, rendered at its full model size
    /// lying on the shelf with the cork end raised toward the front.
    /// </summary>
    public class BlockEntityVialRack : BlockEntityDisplay
    {
        public const int SlotCount = 8;

        private const float TiltDegrees = 67.5f;

        // Per slot, in model pixels: offset applied to the Vial mesh (vial.json's root sits at
        // the origin), then the point the tilt rotates around.
        private static readonly float[][] SlotPose = new float[SlotCount][]
        {
            new float[] { -5f, 9.5f, 3f, -5f, 10f, 3f },
            new float[] { -2f, 9.5f, 3f, -2f, 10f, 3f },
            new float[] { 2f, 9.5f, 3f, 2f, 10f, 3f },
            new float[] { 5f, 9.5f, 3f, 5f, 10f, 3f },
            new float[] { -5f, 15f, 1f, -5f, 15f, 1f },
            new float[] { -2f, 15f, 1f, -2f, 15f, 1f },
            new float[] { 2f, 15f, 1f, 2f, 15f, 1f },
            new float[] { 5f, 15f, 1f, 5f, 15f, 1f }
        };

        private readonly InventoryGeneric inv;

        public override InventoryBase Inventory => inv;

        public override string InventoryClassName => "vialrack";

        public override string AttributeTransformCode => "onvialrackTransform";

        public BlockEntityVialRack()
        {
            inv = new InventoryGeneric(SlotCount, "vialrack-0", null, (id, self) => new ItemSlotVialRack(self));
        }

        public bool IsSlotEmpty(int slotId)
        {
            return slotId >= 0 && slotId < SlotCount && inv[slotId].Empty;
        }

        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            int slotId = blockSel.SelectionBoxIndex;
            if (slotId < 0 || slotId >= SlotCount)
            {
                return false;
            }

            ItemSlot heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (heldSlot.Empty)
            {
                return TryTake(byPlayer, slotId);
            }
            if (heldSlot.Itemstack.Collectible is BlockVial)
            {
                return TryPut(byPlayer, heldSlot, slotId);
            }
            return false;
        }

        private bool TryPut(IPlayer byPlayer, ItemSlot heldSlot, int slotId)
        {
            ItemSlot target = inv[slotId];
            if (!target.Empty)
            {
                (Api as ICoreClientAPI)?.TriggerIngameError(this, "vialrackfull", Lang.Get("remedyandruin:vialrack-full-error"));
                return false;
            }

            int moved = heldSlot.TryPutInto(Api.World, target);
            MarkDirty();
            (Api as ICoreClientAPI)?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            if (moved > 0)
            {
                Api.World.PlaySoundAt(target.Itemstack?.Block?.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, byPlayer.Entity, byPlayer);
                Api.World.Logger.Audit("{0} Put 1x{1} into Vial Rack at {2}.", byPlayer.PlayerName, target.Itemstack?.Collectible.Code, Pos);
                return true;
            }
            return false;
        }

        private bool TryTake(IPlayer byPlayer, int slotId)
        {
            ItemSlot source = inv[slotId];
            if (source.Empty)
            {
                return false;
            }

            ItemStack stack = source.TakeOut(1);
            if (byPlayer.InventoryManager.TryGiveItemstack(stack))
            {
                Api.World.PlaySoundAt(stack.Block?.Sounds?.Place ?? GlobalConstants.DefaultBuildSound, byPlayer.Entity, byPlayer);
            }
            if (stack.StackSize > 0)
            {
                Api.World.SpawnItemEntity(stack, Pos);
            }
            Api.World.Logger.Audit("{0} Took 1x{1} from Vial Rack at {2}.", byPlayer.PlayerName, stack.Collectible.Code, Pos);
            (Api as ICoreClientAPI)?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            MarkDirty();
            return true;
        }

        protected override float[][] genTransformationMatrices()
        {
            float[][] matrices = new float[SlotCount][];
            for (int i = 0; i < SlotCount; i++)
            {
                float[] pose = SlotPose[i];
                matrices[i] = new Matrixf()
                    .Translate(0.5f, 0f, 0.5f)
                    .RotateYDeg(Block.Shape.rotateY)
                    .Translate(-0.5f, 0f, -0.5f)
                    .Translate(pose[3] / 16f, pose[4] / 16f, pose[5] / 16f)
                    .RotateXDeg(TiltDegrees)
                    .Translate(-pose[3] / 16f, -pose[4] / 16f, -pose[5] / 16f)
                    .Translate(pose[0] / 16f, pose[1] / 16f, pose[2] / 16f)
                    .Values;
            }
            return matrices;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            RedrawAfterReceivingTreeAttributes(worldForResolving);
        }

        private class ItemSlotVialRack : ItemSlotDisplay
        {
            public override int MaxSlotStackSize => 1;

            public ItemSlotVialRack(InventoryBase inventory) : base(inventory, "vialrack")
            {
            }

            public override bool CanHold(ItemSlot sourceSlot)
            {
                return sourceSlot?.Itemstack?.Collectible is BlockVial && base.CanHold(sourceSlot);
            }

            public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
            {
                return sourceSlot?.Itemstack?.Collectible is BlockVial && base.CanTakeFrom(sourceSlot, priority);
            }
        }
    }
}
