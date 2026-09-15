using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// The Vial's 9-charge lifecycle (02-design-overview.md §4): a freshly-filled Vial starts at
    /// 9/9. Dipping or crafting a poisoned arrow from it (Plan 11 Tasks 2-3) each consume 1 charge
    /// directly via ItemStack.Attributes, independent of this class - spending the 9th and final
    /// charge also clears the Vial's actual liquid content (SetContent to null), not just the
    /// charge count, so it reads as genuinely empty everywhere, not just to this refill gate.
    /// Drinking always consumes everything remaining in one action (Step 4 of this task, via
    /// Patch_LiquidContainerDrinking) and always fully depletes the Vial to 0/9. This class owns
    /// only the refill half: a Vial cannot be refilled while it still has charges remaining, and a
    /// successful refill always resets to a full 9/9.
    /// </summary>
    public class BlockVial : BlockLiquidContainerTopOpened
    {
        public override int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres)
        {
            if (containerStack.Attributes.GetInt("poisonCharges", 0) > 0)
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
