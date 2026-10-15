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
    /// state, escalation and healing only; tier effects and splinting interactions live elsewhere.
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
    /// Healing: a limb heals while it is injured and either splinted or the Skull (which cannot be
    /// splinted). Full duration in game months by tier: Strain 0.75, Fracture 1.5, Break 1.5, Crush
    /// 2.25, times Calendar.DaysPerMonth. The countdown is one calendar end day per limb in
    /// WatchedAttributes (0 = not healing), so it follows calendar speed and sleep and survives
    /// relogs. It starts at the current tier's full duration when a limb becomes eligible, and is
    /// dropped when the limb stops being eligible. A tier rise unsplints ChestArms and Legs (they must
    /// be re-splinted) and restarts the Skull's countdown. A hit that may injure a healing limb but
    /// does not raise it pushes the end day later by 10% of that tier's duration. When the end day
    /// passes the limb clears entirely to None and unsplinted. One game tick listener per instance
    /// runs while any limb is injured; ResumeAfterLogin re-evaluates all limbs and restarts it.
    ///
    /// GetHealDaysRemaining / SetHealDaysRemaining: read or (debug) overwrite the countdown.
    /// ResumeAfterLogin: server side, call once per login.
    ///
    /// Death clears every limb to None and unsplinted, and every end day to 0.
    ///
    /// Leg effects: ApplyLegEffects maintains one persistent walkspeed modifier (LegWalkStatKey) from
    /// the Legs tier: Strain -0.05, Fracture -0.10, Break or Crush -0.40. It runs on every Legs tier or
    /// splint change, at login and on the heal tick, and removes the modifier when the Legs tier is
    /// None. The modifier never drives the blended walkspeed below 0, because air control reads the
    /// blended value without a lower clamp.
    ///
    /// Movement lock: IsLegLocked is true for an unsplinted Break or Crush Legs injury on a
    /// non-mounted player outside creative and spectator mode. It reads only WatchedAttributes, so it is
    /// valid on the client where this behavior instance does not exist. Player physics is simulated
    /// on the client, so the server cannot veto movement input; the client enforces the lock (see
    /// Remedy_And_RuinModSystem), and the server backstop sets the modifier so the blended walkspeed
    /// is 0.
    /// </summary>
    public class EntityBehaviorBrokenBones : EntityBehavior
    {
        private const float UseEscalationChance = 0.05f;
        private const InjuryTier SkullMaxTier = InjuryTier.Fracture;
        private const int TickIntervalMs = 5000;
        private const double DockFraction = 0.1;

        private const string LegWalkStatKey = "remedyandruinBonesLegs";
        private const float LegStrainWalkPenalty = -0.05f;
        private const float LegFractureWalkPenalty = -0.10f;
        private const float LegBrokenWalkPenalty = -0.40f;

        private long tickListenerId;

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

            InjuryTier clamped = ClampToLimb(limb, tier);
            bool changed = clamped != GetTier(limb);
            entity.WatchedAttributes.SetInt(TierKey(limb), (int)clamped);
            entity.WatchedAttributes.MarkPathDirty(TierKey(limb));

            if (changed) SetEndDay(limb, 0.0);
            RefreshHealing(limb);
            UpdateTickListener();
            if (limb == BoneLocation.Legs) ApplyLegEffects();
        }

        public void SetSplinted(BoneLocation limb, bool splinted)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            entity.WatchedAttributes.SetBool(SplintedKey(limb), splinted);
            entity.WatchedAttributes.MarkPathDirty(SplintedKey(limb));

            RefreshHealing(limb);
            if (limb == BoneLocation.Legs) ApplyLegEffects();
        }

        // True when the entity's own movement input is disabled by an unsplinted Break or Crush Legs
        // injury. Creative, spectator and mounted players are never locked.
        public static bool IsLegLocked(Entity entity)
        {
            if (entity is not EntityPlayer player || entity.WatchedAttributes == null) return false;
            if (player.MountedOn != null) return false;

            EnumGameMode? mode = player.Player?.WorldData?.CurrentGameMode;
            if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator) return false;

            InjuryTier tier = (InjuryTier)entity.WatchedAttributes.GetInt(TierKey(BoneLocation.Legs), (int)InjuryTier.None);
            if (tier < InjuryTier.Break) return false;

            return !entity.WatchedAttributes.GetBool(SplintedKey(BoneLocation.Legs), false);
        }

        // Sets, updates or removes the Legs walkspeed modifier. The modifier is the Legs penalty,
        // limited so the blended walkspeed stays at or above 0; while locked it is the negative of
        // the other modifiers' blended sum (floored at 0), which makes the blended walkspeed 0.
        public void ApplyLegEffects()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            float penalty = GetTier(BoneLocation.Legs) switch
            {
                InjuryTier.Strain => LegStrainWalkPenalty,
                InjuryTier.Fracture => LegFractureWalkPenalty,
                InjuryTier.Break => LegBrokenWalkPenalty,
                InjuryTier.Crush => LegBrokenWalkPenalty,
                _ => 0f
            };

            float others = 0f;
            float? existing = null;
            foreach (System.Collections.Generic.KeyValuePair<string, EntityFloatStats> category in entity.Stats)
            {
                if (category.Key != "walkspeed") continue;

                foreach (System.Collections.Generic.KeyValuePair<string, EntityStat<float>> modifier in category.Value.ValuesByKey)
                {
                    if (modifier.Key == LegWalkStatKey) existing = modifier.Value.Value;
                    else others += modifier.Value.Value * modifier.Value.Weight;
                }
            }

            if (penalty == 0f)
            {
                if (existing != null) entity.Stats.Remove("walkspeed", LegWalkStatKey);
                return;
            }

            float floor = -Math.Max(0f, others);
            float value = IsLegLocked(entity) ? floor : Math.Max(penalty, floor);
            if (existing != null && Math.Abs(existing.Value - value) < 0.00001f) return;

            entity.Stats.Set("walkspeed", LegWalkStatKey, value, persistent: true);
        }

        // Days of healing left, or null when the limb is not healing.
        public double? GetHealDaysRemaining(BoneLocation limb)
        {
            double endDay = GetEndDay(limb);
            if (endDay <= 0.0) return null;

            return Math.Max(0.0, endDay - entity.World.Calendar.TotalDays);
        }

        // Returns false when the limb is not currently healing.
        public bool SetHealDaysRemaining(BoneLocation limb, double days)
        {
            if (entity.World.Side != EnumAppSide.Server) return false;
            if (GetEndDay(limb) <= 0.0) return false;

            SetEndDay(limb, entity.World.Calendar.TotalDays + Math.Max(0.0, days));
            return true;
        }

        public void ResumeAfterLogin(EntityAgent target)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            foreach (BoneLocation limb in Enum.GetValues(typeof(BoneLocation)))
            {
                if (CompleteIfExpired(limb)) continue;
                RefreshHealing(limb);
            }
            UpdateTickListener();
            ApplyLegEffects();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (tickListenerId != 0L)
            {
                entity.World.UnregisterGameTickListener(tickListenerId);
                tickListenerId = 0L;
            }
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
                SetSplinted(limb, false);
                SetTier(limb, InjuryTier.None);
                SetEndDay(limb, 0.0);
            }
            UpdateTickListener();
            ApplyLegEffects();
        }

        public static void HandlePostArmorDamage(EntityPlayer victim, DamageSource source, ref float damage)
        {
            HitClassification? hit = HitContext.Current;
            if (hit == null || !hit.Value.CanInjure) return;

            EntityBehaviorBrokenBones bones = victim.GetBehavior<EntityBehaviorBrokenBones>();
            if (bones == null) return;

            // The jump size is decided by post-armor damage alone for every damage type; revisit when
            // enemies can deal several damage types in one hit.
            BoneLocation limb = hit.Value.Location;
            int jump = RollEscalationJump(damage, victim.World.Rand);
            if (jump <= 0)
            {
                bones.DockHealing(limb);
                return;
            }

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

            if (!bones.Raise(limb, jump)) bones.DockHealing(limb);
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

            bool splintBroke = limb != BoneLocation.Skull && IsSplinted(limb);
            if (limb != BoneLocation.Skull) SetSplinted(limb, false);
            SetTier(limb, after);
            Notify(limb, after);
            if (splintBroke) NotifySplintBroken(limb);
            return true;
        }

        private static double MonthsFor(InjuryTier tier) => tier switch
        {
            InjuryTier.Strain => 0.75,
            InjuryTier.Fracture => 1.5,
            InjuryTier.Break => 1.5,
            InjuryTier.Crush => 2.25,
            _ => 0.0
        };

        private double DurationDays(InjuryTier tier) => MonthsFor(tier) * entity.World.Calendar.DaysPerMonth;

        private bool IsHealEligible(BoneLocation limb) =>
            GetTier(limb) != InjuryTier.None && (limb == BoneLocation.Skull || IsSplinted(limb));

        private double GetEndDay(BoneLocation limb) =>
            entity.WatchedAttributes.GetDouble(EndDayKey(limb), 0.0);

        private void SetEndDay(BoneLocation limb, double endDay)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            entity.WatchedAttributes.SetDouble(EndDayKey(limb), endDay);
            entity.WatchedAttributes.MarkPathDirty(EndDayKey(limb));
        }

        // Starts the full countdown for an eligible limb with none running; drops it for an ineligible one.
        private void RefreshHealing(BoneLocation limb)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            double endDay = GetEndDay(limb);
            if (!IsHealEligible(limb))
            {
                if (endDay != 0.0) SetEndDay(limb, 0.0);
                return;
            }

            if (endDay <= 0.0) SetEndDay(limb, entity.World.Calendar.TotalDays + DurationDays(GetTier(limb)));
        }

        private void DockHealing(BoneLocation limb)
        {
            double endDay = GetEndDay(limb);
            if (endDay <= 0.0) return;

            SetEndDay(limb, endDay + DockFraction * DurationDays(GetTier(limb)));
        }

        private bool CompleteIfExpired(BoneLocation limb)
        {
            double endDay = GetEndDay(limb);
            if (endDay <= 0.0 || entity.World.Calendar.TotalDays < endDay) return false;

            SetSplinted(limb, false);
            SetTier(limb, InjuryTier.None);
            SetEndDay(limb, 0.0);
            NotifyHealed(limb);
            return true;
        }

        private void UpdateTickListener()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            bool injured = false;
            foreach (BoneLocation limb in Enum.GetValues(typeof(BoneLocation)))
            {
                if (GetTier(limb) != InjuryTier.None) { injured = true; break; }
            }

            if (injured && tickListenerId == 0L)
            {
                tickListenerId = entity.World.RegisterGameTickListener(dt => OnHealTick(), TickIntervalMs);
            }
            else if (!injured && tickListenerId != 0L)
            {
                entity.World.UnregisterGameTickListener(tickListenerId);
                tickListenerId = 0L;
            }
        }

        private void OnHealTick()
        {
            foreach (BoneLocation limb in Enum.GetValues(typeof(BoneLocation)))
            {
                CompleteIfExpired(limb);
            }
            UpdateTickListener();
            ApplyLegEffects();
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

        private void NotifyHealed(BoneLocation limb) =>
            SendMessage(limb, Lang.Get("remedyandruin:bones-healed", Lang.Get("remedyandruin:bones-limb-" + LimbName(limb))));

        private void NotifySplintBroken(BoneLocation limb) =>
            SendMessage(limb, Lang.Get("remedyandruin:bones-splint-broken", Lang.Get("remedyandruin:bones-limb-" + LimbName(limb))));

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

        private static string EndDayKey(BoneLocation limb) => "remedyandruinBones" + limb + "HealEndDay";

        private static string SplintedKey(BoneLocation limb) => "remedyandruinBones" + limb + "Splinted";
    }
}
