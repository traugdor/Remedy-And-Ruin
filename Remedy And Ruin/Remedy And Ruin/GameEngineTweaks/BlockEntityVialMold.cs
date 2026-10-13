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
    /// intake to glass-plain and the nine coloured glasses. The base implementation accepts
    /// any stack whose "{metal}" substitution in the mold's "drop" code resolves to an
    /// existing block, so without this override the mold would also accept liquid metals
    /// that have no matching vial.
    /// </summary>
    public class BlockEntityVialMold : BlockEntityToolMold, ILiquidMetalSink
    {
        private static readonly string[] GlassColors =
        {
            "plain", "green", "blue", "violet", "red", "yellow", "brown", "vintage", "smoky", "pink"
        };

        private static bool IsAcceptedGlass(AssetLocation code)
        {
            if (code == null || code.Domain != "game")
            {
                return false;
            }
            foreach (string color in GlassColors)
            {
                if (code.Path == "glass-" + color)
                {
                    return true;
                }
            }
            return false;
        }

        bool ILiquidMetalSink.CanReceive(ItemStack metal)
        {
            if (!IsAcceptedGlass(metal?.Collectible?.Code))
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
