using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Attached to the vanilla arrow item via a JSON patch (behaviors: [{ "name":
    /// "RemedyArrowPoisoning" }]). Overrides the merge-quantity pair the same way
    /// ItemFishingPole special-cases bait being applied to itself - here, a Vial (source) carrying
    /// poison being merged onto an arrow stack (sink) dips exactly one arrow, consuming exactly
    /// one of the Vial's charges, rather than a literal stack-size merge.
    /// </summary>
    public class CollectibleBehaviorArrowPoisoning : CollectibleBehavior
    {
        public CollectibleBehaviorArrowPoisoning(CollectibleObject collObj) : base(collObj) { }

        public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority, ref EnumHandling handling)
        {
            if (priority != EnumMergePriority.DirectMerge)
            {
                handling = EnumHandling.PassThrough;
                return 0;
            }

            (string cluster, float effectMultiplier) = ReadVialPoison(sourceStack);
            if (cluster == null)
            {
                handling = EnumHandling.PassThrough;
                return 0;
            }

            handling = EnumHandling.PreventSubsequent;
            return 1; // always exactly one arrow, regardless of sink stack size
        }

        public override void TryMergeStacks(ItemStackMergeOperation op, ref EnumHandling handling)
        {
            (string cluster, float effectMultiplier) = ReadVialPoison(op.SourceSlot.Itemstack);
            if (cluster == null)
            {
                handling = EnumHandling.PassThrough;
                return;
            }

            int charges = op.SourceSlot.Itemstack.Attributes.GetInt("poisonCharges", 0);
            if (charges <= 0)
            {
                handling = EnumHandling.PreventSubsequent;
                return; // Vial is empty of charges - nothing to dip with
            }

            ItemStack dippedArrow = op.SinkSlot.TakeOut(1);
            dippedArrow.Attributes.SetString("remedyandruinArrowPoisonCluster", cluster);
            dippedArrow.Attributes.SetFloat("remedyandruinArrowPoisonEffectMultiplier", effectMultiplier);
            if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(dippedArrow))
            {
                op.ActingPlayer.Entity.World.SpawnItemEntity(dippedArrow, op.ActingPlayer.Entity.Pos.XYZ);
            }

            int chargesRemaining = charges - 1;
            op.SourceSlot.Itemstack.Attributes.SetInt("poisonCharges", chargesRemaining);
            if (chargesRemaining <= 0)
            {
                // The 9th and final charge was just spent - the Vial is genuinely empty now,
                // not just at 0 charges with liquid still visually present. Matches drinking's
                // own behavior (which already empties content via vanilla's normal drain).
                ((BlockVial)op.SourceSlot.Itemstack.Collectible).SetContent(op.SourceSlot.Itemstack, null);
            }
            op.SourceSlot.MarkDirty();
            op.SinkSlot.MarkDirty();
            handling = EnumHandling.PreventSubsequent;
        }

        private static (string cluster, float effectMultiplier) ReadVialPoison(ItemStack sourceStack)
        {
            if (!(sourceStack?.Collectible is BlockVial)) return (null, 0f);

            ItemStack content = ((BlockVial)sourceStack.Collectible).GetContent(sourceStack);
            JsonObject effectData = content?.Collectible?.Attributes?["remedyandruinEffect"];
            if (effectData == null || !effectData.Exists) return (null, 0f);
            if (!effectData["isPoison"].AsBool()) return (null, 0f);

            return (effectData["cluster"].AsString()?.ToUpper(), effectData["effectMultiplier"].AsFloat());
        }
    }
}
