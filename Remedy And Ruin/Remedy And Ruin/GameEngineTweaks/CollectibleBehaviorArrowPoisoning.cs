using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Attached to the vanilla arrow item via a JSON patch (behaviors: [{ "name":
    /// "RemedyArrowPoisoning" }]). Overrides the merge-quantity pair the same way
    /// ItemFishingPole special-cases bait being applied to itself - here, a Vial (source) carrying
    /// poison being merged onto an arrow stack (sink) dips exactly one arrow, consuming exactly
    /// one of the Vial's charges, rather than a literal stack-size merge.
    ///
    /// Also tints a poisoned arrow's mesh (OnBeforeRender) by multiplying its own normal, already
    /// per-material mesh's vertex colors by the poison cluster's tint - the same vertex-multiply
    /// technique BlockLiquidContainerTopOpened.GenMesh uses for climate color maps, chosen instead
    /// of generating a tinted texture file per material-times-cluster combination (arrows vary by
    /// material via item code, not attributes, so there's no single texture per cluster to swap
    /// the way potions have). Covers held, dropped, and inventory-icon rendering in one pass.
    /// </summary>
    public class CollectibleBehaviorArrowPoisoning : CollectibleBehavior
    {
        private const string MeshCacheKey = "remedyandruinPoisonedArrowMeshes";

        public CollectibleBehaviorArrowPoisoning(CollectibleObject collObj) : base(collObj) { }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            string clusterName = itemstack.Attributes.GetString("remedyandruinArrowPoisonCluster");
            if (clusterName == null || !Enum.TryParse(clusterName, true, out RemedyPoisonCluster cluster))
            {
                return;
            }

            if (!capi.ObjectCache.TryGetValue(MeshCacheKey, out object obj))
            {
                obj = capi.ObjectCache[MeshCacheKey] = new Dictionary<string, MultiTextureMeshRef>();
            }
            Dictionary<string, MultiTextureMeshRef> meshRefs = (Dictionary<string, MultiTextureMeshRef>)obj;

            string cacheKey = itemstack.Collectible.Code.ToShortString() + "-" + cluster;
            if (!meshRefs.TryGetValue(cacheKey, out MultiTextureMeshRef meshRef))
            {
                capi.Tesselator.TesselateItem(itemstack.Item, out MeshData mesh);
                (byte r, byte g, byte b) = RemedyPoisonClusterTextures.GetClusterTint(cluster);
                for (int i = 0; i + 3 < mesh.Rgba.Length; i += 4)
                {
                    mesh.Rgba[i] = (byte)(mesh.Rgba[i] * r / 255);
                    mesh.Rgba[i + 1] = (byte)(mesh.Rgba[i + 1] * g / 255);
                    mesh.Rgba[i + 2] = (byte)(mesh.Rgba[i + 2] * b / 255);
                }
                meshRef = meshRefs[cacheKey] = capi.Render.UploadMultiTextureMesh(mesh);
            }
            renderinfo.ModelRef = meshRef;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            string clusterName = inSlot.Itemstack.Attributes.GetString("remedyandruinArrowPoisonCluster");
            if (clusterName == null || !Enum.TryParse(clusterName, true, out RemedyPoisonCluster cluster))
            {
                return;
            }

            string clusterDisplayName = Lang.Get("item-potion-" + cluster.ToString().ToLowerInvariant());
            dsc.AppendLine(Lang.Get("arrow-poisoned", clusterDisplayName));
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (!(api is ICoreClientAPI capi) || !capi.ObjectCache.TryGetValue(MeshCacheKey, out object obj))
            {
                return;
            }
            foreach (MultiTextureMeshRef meshRef in ((Dictionary<string, MultiTextureMeshRef>)obj).Values)
            {
                meshRef.Dispose();
            }
            capi.ObjectCache.Remove(MeshCacheKey);
        }

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

        public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe, ref EnumHandling bhHandling)
        {
            ItemSlot vialSlot = System.Array.Find(allInputSlots, slot => slot.Itemstack?.Collectible is BlockVial);
            if (vialSlot == null)
            {
                bhHandling = EnumHandling.PassThrough;
                return;
            }

            ItemStack vialStack = vialSlot.Itemstack;
            int charges = vialStack.Attributes.GetInt("poisonCharges", 0);
            if (charges <= 0)
            {
                bhHandling = EnumHandling.PassThrough;
                return;
            }

            ItemStack content = ((BlockVial)vialStack.Collectible).GetContent(vialStack);
            JsonObject effectData = content?.Collectible?.Attributes?["remedyandruinEffect"];
            if (effectData == null || !effectData.Exists || !effectData["isPoison"].AsBool())
            {
                bhHandling = EnumHandling.PassThrough;
                return;
            }

            outputSlot.Itemstack.Attributes.SetString("remedyandruinArrowPoisonCluster", effectData["cluster"].AsString()?.ToUpper());
            outputSlot.Itemstack.Attributes.SetFloat("remedyandruinArrowPoisonEffectMultiplier", effectData["effectMultiplier"].AsFloat());
            int chargesRemaining = charges - 1;
            vialStack.Attributes.SetInt("poisonCharges", chargesRemaining);
            if (chargesRemaining <= 0)
            {
                // Same rule as dipping (Task 2): spending the last charge genuinely empties the Vial.
                ((BlockVial)vialStack.Collectible).SetContent(vialStack, null);
            }
            vialSlot.MarkDirty();
            bhHandling = EnumHandling.PreventSubsequent;
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
