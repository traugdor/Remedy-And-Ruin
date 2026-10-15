using System;
using Remedy_And_Ruin.GameEngineTweaks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.RRBehaviors
{
    public enum InjuryTier
    {
        None = 0,
        Strain = 1,
        Fracture = 2,
        Break = 3,
        Crush = 4
    }

    /// <summary>
    /// Owns the per-limb bone injury state (Skull, ChestArms, Legs) and its escalation. This class holds
    /// state and escalation only; tier effects, healing and splinting interactions live elsewhere.
    ///
    /// State: each limb has an InjuryTier and a splinted flag in WatchedAttributes, so both survive
    /// saves and relogs and are readable on the client. ChestArms and Legs reach Crush; Skull stops at
    /// Fracture, and an escalation that would take it past Fracture is fatal (see ExceedsSkullLimit).
    ///
    /// Hit escalation: HandlePostArmorDamage is a PostArmorDamagePipeline handler. For a hit that may
    /// injure, the post-armor damage picks a chance and a jump size; a successful roll raises the
    /// limb's current tier by the jump, capped at Crush. A fatal skull injury raises the damage past
    /// the victim's health so vanilla's own death handling runs. Register it before any handler that
    /// reduces the damage, so it sees the full post-armor amount.
    ///
    /// Use escalation: RollUseEscalation gives an injured limb a flat chance of one more tier. Callers
    /// decide who may roll.
    ///
    /// GetTier / IsSplinted: read the state. SetTier / SetSplinted: write it, server only, no message.
    /// FindWorstSplintableLimb / TrySplintWorstLimb: pick and splint the worst injured, unsplinted limb.
    ///
    /// Death clears every limb to None and unsplinted.
    /// </summary>
    public class EntityBehaviorBrokenBones : EntityBehavior
    {
        private const float UseEscalationChance = 0.05f;
        private const InjuryTier SkullMaxTier = InjuryTier.Fracture;

        public EntityBehaviorBrokenBones(Entity entity) : base(entity)
        {
        }

        public override string PropertyName() => "remedyandruinBrokenBones";

        public InjuryTier GetTier(BoneLocation limb) =>
            (InjuryTier)entity.WatchedAttributes.GetInt(TierKey(limb), (int)InjuryTier.None);

        public bool IsSplinted(BoneLocation limb) =>
            entity.WatchedAttributes.GetBool(SplintedKey(limb), false);

        public void SetTier(BoneLocation limb, InjuryTier tier)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            entity.WatchedAttributes.SetInt(TierKey(limb), (int)ClampToLimb(limb, tier));
            entity.WatchedAttributes.MarkPathDirty(TierKey(limb));
        }

        public void SetSplinted(BoneLocation limb, bool splinted)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            entity.WatchedAttributes.SetBool(SplintedKey(limb), splinted);
            entity.WatchedAttributes.MarkPathDirty(SplintedKey(limb));
        }

        // The injured, unsplinted limb with the highest tier among Legs and ChestArms (Legs wins a tie).
        // The skull is never splintable.
        public BoneLocation? FindWorstSplintableLimb()
        {
            BoneLocation? worst = null;
            InjuryTier worstTier = InjuryTier.None;
            foreach (BoneLocation limb in new[] { BoneLocation.Legs, BoneLocation.ChestArms })
            {
                InjuryTier tier = GetTier(limb);
                if (tier == InjuryTier.None || IsSplinted(limb) || tier <= worstTier) continue;

                worst = limb;
                worstTier = tier;
            }
            return worst;
        }

        // Splints the limb FindWorstSplintableLimb picks and tells the player; returns it, or null when
        // no limb qualifies.
        public BoneLocation? TrySplintWorstLimb()
        {
            if (entity.World.Side != EnumAppSide.Server) return null;

            BoneLocation? limb = FindWorstSplintableLimb();
            if (limb == null) return null;

            SetSplinted(limb.Value, true);
            NotifySplinted(limb.Value);
            return limb;
        }

        public static InjuryTier MaxTier(BoneLocation limb) =>
            limb == BoneLocation.Skull ? SkullMaxTier : InjuryTier.Crush;

        // The single definition of the skull's fatal overshoot: a jump that would end above the skull's
        // maximum tier kills.
        public static bool ExceedsSkullLimit(InjuryTier current, int jump) =>
            (int)current + jump > (int)SkullMaxTier;

        public bool RollUseEscalation(BoneLocation limb)
        {
            if (entity.World.Side != EnumAppSide.Server) return false;
            if (limb == BoneLocation.Skull) return false;

            InjuryTier current = GetTier(limb);
            if (current == InjuryTier.None) return false;
            if (entity.World.Rand.NextDouble() >= UseEscalationChance) return false;

            return Raise(limb, 1);
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            foreach (BoneLocation limb in Enum.GetValues(typeof(BoneLocation)))
            {
                SetTier(limb, InjuryTier.None);
                SetSplinted(limb, false);
            }
        }

        public static void HandlePostArmorDamage(EntityPlayer victim, DamageSource source, ref float damage)
        {
            HitClassification? hit = HitContext.Current;
            if (hit == null || !hit.Value.CanInjure) return;

            EntityBehaviorBrokenBones bones = victim.GetBehavior<EntityBehaviorBrokenBones>();
            if (bones == null) return;

            // The jump size is decided by post-armor damage alone for every damage type; revisit when
            // enemies can deal several damage types in one hit.
            int jump = RollEscalationJump(damage, victim.World.Rand);
            if (jump <= 0) return;

            BoneLocation limb = hit.Value.Location;
            if (limb == BoneLocation.Skull && ExceedsSkullLimit(bones.GetTier(limb), jump))
            {
                EntityBehaviorHealth health = victim.GetBehavior<EntityBehaviorHealth>();
                if (health != null)
                {
                    damage = Math.Max(damage, health.Health + 1f);
                    HitContext.Lethal = true;
                }
                return;
            }

            bones.Raise(limb, jump);
        }

        // Returns the tier jump for a hit of the given post-armor damage, or 0 when the roll fails.
        private static int RollEscalationJump(float damage, Random rand)
        {
            float chance;
            int minJump;
            int maxJump;
            if (damage < 4f) { chance = 0.05f; minJump = 1; maxJump = 1; }
            else if (damage < 8f) { chance = 0.15f; minJump = 1; maxJump = 1; }
            else if (damage < 12f) { chance = 0.30f; minJump = 1; maxJump = 2; }
            else { chance = 0.50f; minJump = 2; maxJump = 3; }

            if (rand.NextDouble() >= chance) return 0;

            return rand.Next(minJump, maxJump + 1);
        }

        private bool Raise(BoneLocation limb, int jump)
        {
            InjuryTier before = GetTier(limb);
            InjuryTier after = ClampToLimb(limb, (InjuryTier)((int)before + jump));
            if (after <= before) return false;

            SetTier(limb, after);
            Notify(limb, after);
            return true;
        }

        private void Notify(BoneLocation limb, InjuryTier tier)
        {
            string text = Lang.Get("remedyandruin:bones-tier-raised",
                Lang.Get("remedyandruin:bones-limb-" + LimbName(limb)),
                Lang.Get("remedyandruin:bones-tier-" + tier.ToString().ToLowerInvariant()));
            SendMessage(limb, text);
        }

        private void NotifySplinted(BoneLocation limb) =>
            SendMessage(limb, Lang.Get("remedyandruin:bones-splinted", Lang.Get("remedyandruin:bones-limb-" + LimbName(limb))));

        private void SendMessage(BoneLocation limb, string text)
        {
            if (entity is not EntityPlayer player || player.Player is not IServerPlayer serverPlayer) return;

            serverPlayer.SendIngameError("remedyandruinBones" + LimbName(limb), text);
            if (Remedy_And_RuinModSystem.Config?.injuryChatMessagesEnabled ?? true)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, text, EnumChatType.Notification);
            }
        }

        private static InjuryTier ClampToLimb(BoneLocation limb, InjuryTier tier)
        {
            InjuryTier max = MaxTier(limb);
            if (tier > max) return max;
            if (tier < InjuryTier.None) return InjuryTier.None;
            return tier;
        }

        private static string LimbName(BoneLocation limb) => limb switch
        {
            BoneLocation.Skull => "skull",
            BoneLocation.Legs => "legs",
            _ => "arms"
        };

        private static string TierKey(BoneLocation limb) => "remedyandruinBones" + limb + "Tier";

        private static string SplintedKey(BoneLocation limb) => "remedyandruinBones" + limb + "Splinted";
    }
}
