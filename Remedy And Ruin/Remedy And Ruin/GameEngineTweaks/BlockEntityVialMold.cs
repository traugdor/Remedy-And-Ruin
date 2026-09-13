using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// BlockEntityToolMold reused for the Vial Mold. Pouring, cooling, and taking
    /// contents are all inherited as-is.
    ///
    /// GetBlockInfo overrides the base class's "units of metal" wording with "units of
    /// glass".
    ///
    /// CanReceive is re-implemented via explicit ILiquidMetalSink.CanReceive, not a plain
    /// override, because BlockEntityToolMold.CanReceive is not virtual; callers such as
    /// BlockSmeltedContainer invoke it only through the ILiquidMetalSink interface, so an
    /// explicit interface implementation is the only way to intercept it. It restricts
    /// intake to glass-plain - the base implementation has no material check (the mold's
    /// "drop" attribute is a single fixed code with no "{metal}" placeholder), so without
    /// this override the mold would accept any liquid metal.
    /// </summary>
    public class BlockEntityVialMold : BlockEntityToolMold, ILiquidMetalSink
    {
        private static readonly AssetLocation AcceptedMaterial = new AssetLocation("game", "glass-plain");

        bool ILiquidMetalSink.CanReceive(ItemStack metal) // even though it's really glass... See ^^^
        {
            if (metal?.Collectible?.Code == null || !metal.Collectible.Code.Equals(AcceptedMaterial))
            {
                return false;
            }
            return CanReceive(metal);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (Shattered)
            {
                base.GetBlockInfo(forPlayer, dsc);
                return;
            }

            string state = IsLiquid ? Lang.Get("liquid") : (IsHardened ? Lang.Get("hardened") : Lang.Get("soft"));
            string temp = Temperature < 21f ? Lang.Get("Cold") : Lang.Get("{0}°C", (int)Temperature);
            if (MetalContent != null)
            {
                dsc.AppendLine(Lang.Get("{0}/{1} units of glass {2} ({3})", FillLevel, requiredUnits, state, temp) + "\n");
            }
            else
            {
                dsc.AppendLine(Lang.Get("0/{0} units of glass", requiredUnits) + "\n");
            }
        }
    }
}
