using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// The Vial's 9-charge lifecycle (02-design-overview.md §4): a freshly-filled Vial starts at
    /// 9/9. Dipping an arrow in it consumes 1 charge directly via
    /// ItemStack.Attributes, independent of this class - spending the 9th and final charge also
    /// clears the Vial's actual liquid content (SetContent to null), not just the charge count, so
    /// it reads as genuinely empty everywhere, not just to this refill gate. Drinking always
    /// consumes everything remaining in one action (via Patch_LiquidContainerDrinking) and always
    /// fully depletes the Vial to 0/9. This class owns only the refill half: a Vial cannot be
    /// refilled while it still has charges remaining, and a successful refill always resets to a
    /// full 9/9.
    ///
    /// SetContents: every way a Vial's content is removed (pouring out, spilling, drinking, the
    /// final dip) funnels through this virtual method, so an emptied Vial always loses its
    /// poisonCharges here. This is an override rather than a patch on the non-virtual SetContent
    /// because the JIT can inline SetContent into its callers and skip a patch on it.
    ///
    /// OnBeforeRender: held, inventory and dropped Vials are drawn by the item shader, which writes
    /// depth for every non-transparent glass pixel. The liquid sits entirely inside the glass, so
    /// vanilla's order (glass first, liquid appended) depth-rejects it. The mesh built here puts
    /// the liquid first and the glass after it. Rack and ground rendering go through the chunk
    /// renderer and are unaffected.
    /// </summary>
    public class BlockVial : BlockLiquidContainerTopOpened
    {
        private Vintagestory.API.Common.Shape liquidShape;
        private Vintagestory.API.Common.Shape opaqueShape;

        private string LiquidFirstMeshCacheKey => Code.ToShortString() + "liquidFirstMeshRefs";

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            ItemStack contentStack = GetContent(itemstack);
            if (contentStack == null || GetContainableProps(contentStack) == null)
            {
                base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                return;
            }

            if (!capi.ObjectCache.TryGetValue(LiquidFirstMeshCacheKey, out object cached))
            {
                cached = capi.ObjectCache[LiquidFirstMeshCacheKey] = new Dictionary<int, MultiTextureMeshRef>();
            }
            Dictionary<int, MultiTextureMeshRef> meshRefs = (Dictionary<int, MultiTextureMeshRef>)cached;

            int hash = GetStackCacheHashCode(contentStack);
            if (!meshRefs.TryGetValue(hash, out MultiTextureMeshRef meshRef))
            {
                meshRef = meshRefs[hash] = capi.Render.UploadMultiTextureMesh(GenLiquidFirstMesh(capi, contentStack));
            }
            renderinfo.ModelRef = meshRef;
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            if (api is ICoreClientAPI capi && capi.ObjectCache.TryGetValue(LiquidFirstMeshCacheKey, out object cached))
            {
                foreach (MultiTextureMeshRef meshRef in ((Dictionary<int, MultiTextureMeshRef>)cached).Values)
                {
                    meshRef.Dispose();
                }
                capi.ObjectCache.Remove(LiquidFirstMeshCacheKey);
            }
        }

        // Same liquid tessellation as BlockLiquidContainerTopOpened.GenMesh, with the glass mesh
        // appended after the liquid instead of before it.
        private MeshData GenLiquidFirstMesh(ICoreClientAPI capi, ItemStack contentStack)
        {
            MeshData glassMesh = GenMesh(capi, null);
            WaterTightContainableProps props = GetContainableProps(contentStack);

            ref Vintagestory.API.Common.Shape cachedShape = ref (props.IsOpaque ? ref opaqueShape : ref liquidShape);
            AssetLocation shapeLoc = props.IsOpaque ? contentShapeLoc : liquidContentShapeLoc;
            if (cachedShape == null)
            {
                cachedShape = Vintagestory.API.Common.Shape.TryGet(capi, shapeLoc.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/"));
            }
            if (cachedShape == null)
            {
                return GenMesh(capi, contentStack);
            }

            ContainerTextureSource contentSource = new ContainerTextureSource(capi, contentStack, props.Texture);
            capi.Tesselator.TesselateShape(GetType().Name, cachedShape, out MeshData liquidMesh, contentSource, new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ), props.GlowLevel, 0, 0);
            liquidMesh.Translate(0f, GameMath.Min(liquidMaxYTranslate, contentStack.StackSize / props.ItemsPerLitre * liquidYTranslatePerLitre), 0f);

            if (props.ClimateColorMap != null)
            {
                int color = capi.World.ApplyColorMapOnRgba(props.ClimateColorMap, null, -1, 196, 128, flipRb: false);
                byte[] bgra = ColorUtil.ToBGRABytes(color);
                for (int i = 0; i < liquidMesh.Rgba.Length; i++)
                {
                    liquidMesh.Rgba[i] = (byte)(liquidMesh.Rgba[i] * bgra[i % 4] / 255);
                }
            }
            for (int i = 0; i < liquidMesh.FlagsCount; i++)
            {
                liquidMesh.Flags[i] &= -4097;
            }

            liquidMesh.AddMeshData(glassMesh);
            return liquidMesh;
        }

        public override void SetContents(ItemStack containerStack, ItemStack[] stacks)
        {
            base.SetContents(containerStack, stacks);

            if (stacks == null || stacks.Length == 0 || System.Array.TrueForAll(stacks, stack => stack == null))
            {
                containerStack.Attributes.RemoveAttribute("poisonCharges");
            }
        }

        public override int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres)
        {
            if (GetContent(containerStack) != null && containerStack.Attributes.GetInt("poisonCharges", 0) > 0)
            {
                return 0; // still has charges remaining - not empty, can't be refilled yet
            }

            int litresPut = base.TryPutLiquid(containerStack, liquidStack, desiredLitres);
            if (litresPut > 0)
            {
                containerStack.Attributes.SetInt("poisonCharges", 9);
            }
            return litresPut;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (GetContent(inSlot.Itemstack) != null)
            {
                int charges = inSlot.Itemstack.Attributes.GetInt("poisonCharges", 9);
                dsc.AppendLine(Lang.Get("remedyandruin:vial-charges", charges));
            }
        }
    }
}
