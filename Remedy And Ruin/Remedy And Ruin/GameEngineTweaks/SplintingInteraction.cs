using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Splinting by right-click: at least 4 sticks in the offhand plus, in the active hotbar slot,
    /// either one clean linen bandage (bandage-clean) or at least 3 ropes. When a limb can be splinted
    /// (EntityBehaviorBrokenBones.FindWorstSplintableLimb), the matching materials are consumed and
    /// that limb is splinted. Otherwise the click is not touched and the held item behaves as in vanilla.
    ///
    /// Start / Stop: subscribe and unsubscribe the server HandInteract event. The handler acts on
    /// StartHeldItemUse and sets PreventDefault, which makes the server skip the packet entirely so the
    /// held item's own use (healing, rope tying) never starts. The server does not look at the player's
    /// game mode when it consumes a bandage, and neither does this.
    /// </summary>
    public static class SplintingInteraction
    {
        private const int SticksRequired = 4;
        private const int RopesRequired = 3;

        private static ICoreServerAPI serverApi;

        public static void Start(ICoreServerAPI api)
        {
            Stop();
            serverApi = api;
            api.Event.HandInteract += OnHandInteract;
        }

        public static void Stop()
        {
            if (serverApi != null) serverApi.Event.HandInteract -= OnHandInteract;
            serverApi = null;
        }

        private static void OnHandInteract(IServerPlayer player, EnumHandInteractNw action, float secondsPassed, ref EnumHandling handling)
        {
            if (action != EnumHandInteractNw.StartHeldItemUse) return;

            EntityPlayer entity = player.Entity;
            ItemSlot offhand = entity?.LeftHandItemSlot;
            ItemSlot mainhand = player.InventoryManager.ActiveHotbarSlot;
            if (offhand?.Itemstack == null || mainhand?.Itemstack == null) return;
            if (!IsItem(offhand.Itemstack, "stick") || offhand.StackSize < SticksRequired) return;

            int mainCost;
            if (IsItem(mainhand.Itemstack, "bandage-clean")) mainCost = 1;
            else if (IsItem(mainhand.Itemstack, "rope") && mainhand.StackSize >= RopesRequired) mainCost = RopesRequired;
            else return;

            EntityBehaviorBrokenBones bones = entity.GetBehavior<EntityBehaviorBrokenBones>();
            if (bones == null || bones.TrySplintWorstLimb() == null) return;

            offhand.TakeOut(SticksRequired);
            offhand.MarkDirty();
            mainhand.TakeOut(mainCost);
            mainhand.MarkDirty();
            handling = EnumHandling.PreventDefault;
        }

        private static bool IsItem(ItemStack stack, string path) =>
            stack.Collectible?.Code != null && stack.Collectible.Code.Domain == GlobalConstants.DefaultDomain && stack.Collectible.Code.Path == path;
    }
}
