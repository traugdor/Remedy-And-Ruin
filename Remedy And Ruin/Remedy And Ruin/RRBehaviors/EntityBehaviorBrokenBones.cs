using System;
using Remedy_And_Ruin.GameEngineTweaks;
using Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.RRBehaviors
{
    /// <summary>Kinds of use event that can roll a use escalation (see RollFromUse).</summary>
    public enum UseKind
    {
        ArmUse = 0,
        Footstep = 1,
        Jump = 2,
        HardLanding = 3
    }

    public enum InjuryTier
    {
        None = 0,
        Strain = 1,
        Fracture = 2,
        Break = 3,
        Crush = 4
    }

    /// <summary>Offhand restriction level imposed by a broken ChestArms limb.</summary>
    public enum OffhandMode
    {
        Free = 0,
        Locked = 1
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
    /// decide who may roll; RollFromUse is the policy for real use events (ArmUse, Footstep, Jump,
    /// HardLanding). Hard landings are rolled here from HandlePostArmorDamage for fall damage.
    /// DebugUseChancePercent and DebugStepsPerRoll are non-persistent debug overrides for use rolls.
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
    ///
    /// Arm effects: WorkSpeedMultiplier maps the ChestArms state to a work-speed factor: Strain 0.95,
    /// Fracture 0.90, Break or Crush 0.60 splinted and 0.50 unsplinted (one injured arm means
    /// everything is done one-handed). It reads only WatchedAttributes, so it is valid on the client
    /// and ignores game mode. ApplyArmEffects mirrors it into one persistent meleeWeaponsDamage
    /// modifier (ArmMeleeStatKey) on every ChestArms tier or splint change, at login, on the heal tick
    /// and on death; the modifier never drives the blended stat below 0. Block-break speed is
    /// computed on the client and is scaled there (see Patch_ArmDigSpeed).
    ///
    /// Skull effects: ApplySkullEffects keeps a -10 max-health modifier (SkullMaxHealthKey) while the Skull
    /// is at Fracture and removes it otherwise. Vanilla does not persist max-health modifiers, so it
    /// runs on every Skull tier change and at login, and limits current health to the new maximum.
    ///
    /// Bow rules: a bow takes two working arms. HasBrokenArm is true for a Break or Crush ChestArms
    /// injury on a player outside creative and spectator mode. IsBowDrawBlocked is true when that arm
    /// is unsplinted and the server setting allowBowWithUnsplintedBrokenArm is off. Whenever HasBrokenArm
    /// is true and the draw is not blocked, draw time and arrow damage carry the
    /// brokenArmBowPenaltyPercent penalty (see BowDrawPenaltyDivisor and BowDamageMultiplier).
    ///
    /// Offhand rules: GetOffhandMode is Locked for an unsplinted Break or Crush ChestArms injury and
    /// Free otherwise, including a splinted arm and creative and spectator mode; it reads only
    /// WatchedAttributes, so the client-side slot guard can use it (see OffhandRules and
    /// Patch_OffhandGuard). EnforceOffhand (server) drops an offhand stack the mode no longer allows;
    /// it runs on every ChestArms tier or splint change, at login and on the heal tick.
    ///
    /// Shield blocks: OnShieldBlock (server) is called when a shield absorbed part of a hit on this
    /// player (see Patch_ShieldBlockBreaksSplint). While ChestArms is splinted it has a
    /// ShieldBlockSplintBreakChance chance to break the splint without changing the tier, drop that
    /// shield and tell the player.
    ///
    /// Bell headache: a self-resolving condition, not a Skull tier, that never changes any tier or
    /// health. BellProximity starts it after sustained exposure to a ringing bell (StartBellHeadache).
    /// Its end day (BellHeadacheEndDayKey, in-game days, 0 = none) is stored in WatchedAttributes, so
    /// it follows calendar speed and sleep and survives relogs. TickBellHeadache, called each
    /// BellProximity tick, pushes the end day to BellHeadacheLingerDays after now while the player is
    /// still exposed and sends the end message once when it runs out. IsBellHeadacheActive reads only
    /// WatchedAttributes and the calendar, so it is valid on the client. ResumeAfterLogin reconciles an
    /// already-expired headache silently; death clears it.
    /// </summary>
    public class EntityBehaviorBrokenBones : EntityBehavior
    {
        private const float UseEscalationChance = 0.05f;

        /// <summary>Debug override of the use-roll chance in percent; null uses the default. Static, never saved.</summary>
        public static float? DebugUseChancePercent;

        /// <summary>Debug override of footsteps per leg use roll; null uses the computed value. Static, never saved.</summary>
        public static int? DebugStepsPerRoll;
        private const InjuryTier SkullMaxTier = InjuryTier.Fracture;
        private const int TickIntervalMs = 5000;
        private const double DockFraction = 0.1;
        private const float ShieldBlockSplintBreakChance = 0.25f;

        // Played when a hit raises a limb to Fracture or above; Strain is a sprain, not a break.
        private static readonly AssetLocation BoneBreakSound = new AssetLocation("sounds/effect/toolbreak");

        private const string LegWalkStatKey = "remedyandruinBonesLegs";
        private const float LegStrainWalkPenalty = -0.05f;
        private const float LegFractureWalkPenalty = -0.10f;
        private const float LegBrokenWalkPenalty = -0.40f;

        private const string ArmMeleeStatKey = "remedyandruinBonesArms";

        private const string SkullMaxHealthKey = "remedyandruinBonesSkull";
        private const float SkullFractureMaxHealthPenalty = -10f;

        private const string BellHeadacheEndDayKey = "remedyandruinBellHeadacheEndDay";
        private const double BellHeadacheLingerDays = 1.0 / 24.0;

        private long tickListenerId;

        // In memory only; true while the end message for the current headache is still owed.
        private bool bellHeadacheAnnounced;

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
            else if (limb == BoneLocation.Skull) ApplySkullEffects();
            else if (limb == BoneLocation.ChestArms)
            {
                ApplyArmEffects();
                EnforceOffhand();
            }
        }

        public void SetSplinted(BoneLocation limb, bool splinted)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            entity.WatchedAttributes.SetBool(SplintedKey(limb), splinted);
            entity.WatchedAttributes.MarkPathDirty(SplintedKey(limb));

            RefreshHealing(limb);
            if (limb == BoneLocation.Legs) ApplyLegEffects();
            else if (limb == BoneLocation.ChestArms)
            {
                ApplyArmEffects();
                EnforceOffhand();
            }
        }

        // Work-speed factor for digging and melee damage from the entity's ChestArms state; 1 when
        // uninjured. Independent of game mode.
        public static float WorkSpeedMultiplier(Entity entity)
        {
            if (entity?.WatchedAttributes == null) return 1f;

            switch (ReadTier(entity, BoneLocation.ChestArms))
            {
                case InjuryTier.Strain: return 0.95f;
                case InjuryTier.Fracture: return 0.90f;
                case InjuryTier.Break:
                case InjuryTier.Crush:
                    return ReadSplinted(entity, BoneLocation.ChestArms) ? 0.60f : 0.50f;
                default: return 1f;
            }
        }

        // True for a Break or Crush ChestArms injury on a player outside creative and spectator mode.
        // Mounted players are not exempt.
        public static bool HasBrokenArm(Entity entity)
        {
            if (entity is not EntityPlayer player || entity.WatchedAttributes == null) return false;

            EnumGameMode? mode = player.Player?.WorldData?.CurrentGameMode;
            if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator) return false;

            return ReadTier(entity, BoneLocation.ChestArms) >= InjuryTier.Break;
        }

        // True when the entity may not draw a bow: a broken, unsplinted arm with the server setting off.
        public static bool IsBowDrawBlocked(Entity entity)
        {
            if (!HasBrokenArm(entity)) return false;
            if (ReadSplinted(entity, BoneLocation.ChestArms)) return false;

            return !ConfigServer.AllowBowWithUnsplintedBrokenArm(entity.World);
        }

        // Factor a bow draw time is divided by; 1 when no bow penalty applies.
        public static float BowDrawPenaltyDivisor(Entity entity) =>
            HasBrokenArm(entity) ? 1f + ConfigServer.BrokenArmBowPenaltyPercent(entity.World) / 100f : 1f;

        // Factor arrow damage is multiplied by; 1 when no bow penalty applies.
        public static float BowDamageMultiplier(Entity entity) =>
            HasBrokenArm(entity) ? 1f - ConfigServer.BrokenArmBowPenaltyPercent(entity.World) / 100f : 1f;

        // Offhand restriction for the entity: Locked for an unsplinted broken arm, Free otherwise
        // (including a splinted arm and creative and spectator players).
        public static OffhandMode GetOffhandMode(Entity entity)
        {
            if (!HasBrokenArm(entity)) return OffhandMode.Free;

            return ReadSplinted(entity, BoneLocation.ChestArms) ? OffhandMode.Free : OffhandMode.Locked;
        }

        // Drops the offhand stack when the current offhand mode no longer allows it. Server only.
        public void EnforceOffhand()
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;
            if (entity is not EntityPlayer player || player.Player is not IServerPlayer serverPlayer) return;

            OffhandMode mode = GetOffhandMode(entity);
            if (mode == OffhandMode.Free) return;

            ItemSlot slot = serverPlayer.InventoryManager?.OffhandHotbarSlot;
            if (slot?.Itemstack == null || OffhandRules.Allows(mode, slot.Itemstack)) return;

            if (serverPlayer.InventoryManager.DropItem(slot, true))
            {
                SendMessage(BoneLocation.ChestArms, Lang.Get("remedyandruin:bones-offhand-dropped"));
            }
        }

        // Called after a shield in shieldSlot absorbed part of a hit on this player. Server only. With
        // a splinted ChestArms (any tier) the splint breaks with ShieldBlockSplintBreakChance: the
        // tier is unchanged, the shield is dropped from the hand and the player is told. The drop
        // happens before the splint state changes so EnforceOffhand finds the slot already empty.
        public void OnShieldBlock(ItemSlot shieldSlot, bool force = false)
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;
            if (entity is not EntityPlayer player || player.Player is not IServerPlayer serverPlayer) return;
            if (shieldSlot?.Itemstack == null) return;
            if (GetTier(BoneLocation.ChestArms) == InjuryTier.None || !IsSplinted(BoneLocation.ChestArms)) return;
            if (!force && entity.World.Rand.NextDouble() >= ShieldBlockSplintBreakChance) return;

            serverPlayer.InventoryManager.DropItem(shieldSlot, true);
            SetSplinted(BoneLocation.ChestArms, false);
            SendMessage(BoneLocation.ChestArms, Lang.Get("remedyandruin:bones-splint-broken-shield"));
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

            ReadStatModifiers("walkspeed", LegWalkStatKey, out float others, out float? existing);

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

        // Sets, updates or removes the ChestArms meleeWeaponsDamage modifier: the work-speed factor minus
        // 1, limited so the blended stat stays at or above 0.
        public void ApplyArmEffects()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            float penalty = WorkSpeedMultiplier(entity) - 1f;
            ReadStatModifiers("meleeWeaponsDamage", ArmMeleeStatKey, out float others, out float? existing);

            if (penalty == 0f)
            {
                if (existing != null) entity.Stats.Remove("meleeWeaponsDamage", ArmMeleeStatKey);
                return;
            }

            float value = Math.Max(penalty, -Math.Max(0f, others));
            if (existing != null && Math.Abs(existing.Value - value) < 0.00001f) return;

            entity.Stats.Set("meleeWeaponsDamage", ArmMeleeStatKey, value, persistent: true);
        }

        // Applies or removes the Skull Fracture max-health penalty. Vanilla does not persist max-health
        // modifiers, so this runs again on login, and current health is limited to the new maximum
        // because vanilla only lowers it for a player who was at full health.
        public void ApplySkullEffects()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            EntityBehaviorHealth health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;

            bool fractured = GetTier(BoneLocation.Skull) >= InjuryTier.Fracture;
            health.SetMaxHealthModifiers(SkullMaxHealthKey, fractured ? SkullFractureMaxHealthPenalty : 0f);
            if (health.Health > health.MaxHealth) health.Health = health.MaxHealth;
        }

        // Sums the other modifiers' weighted values in a stat category and reads this mod's own value.
        private void ReadStatModifiers(string category, string ownKey, out float others, out float? existing)
        {
            others = 0f;
            existing = null;
            foreach (System.Collections.Generic.KeyValuePair<string, EntityFloatStats> entry in entity.Stats)
            {
                if (entry.Key != category) continue;

                foreach (System.Collections.Generic.KeyValuePair<string, EntityStat<float>> modifier in entry.Value.ValuesByKey)
                {
                    if (modifier.Key == ownKey) existing = modifier.Value.Value;
                    else others += modifier.Value.Value * modifier.Value.Weight;
                }
            }
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

        // True while the headache's end day is in the future. Valid on both sides.
        public static bool IsBellHeadacheActive(Entity entity)
        {
            if (entity?.WatchedAttributes == null || entity.World?.Calendar == null) return false;

            double endDay = entity.WatchedAttributes.GetDouble(BellHeadacheEndDayKey, 0.0);
            return endDay > 0.0 && entity.World.Calendar.TotalDays < endDay;
        }

        // In-game days of headache left, or null when none. Server or client.
        public static double? GetBellHeadacheDaysRemaining(Entity entity) =>
            IsBellHeadacheActive(entity)
                ? entity.WatchedAttributes.GetDouble(BellHeadacheEndDayKey, 0.0) - entity.World.Calendar.TotalDays
                : null;

        // Starts the headache for BellHeadacheLingerDays and tells the player, unless one is already active.
        public void StartBellHeadache()
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return;
            if (IsBellHeadacheActive(entity)) return;

            SetBellHeadacheEndDay(entity.World.Calendar.TotalDays + BellHeadacheLingerDays);
            bellHeadacheAnnounced = true;
            SendBellMessage(Lang.Get("remedyandruin:bones-bell-headache"));
        }

        // Ends the headache without a message.
        public void ClearBellHeadache()
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            bellHeadacheAnnounced = false;
            if (entity.WatchedAttributes.GetDouble(BellHeadacheEndDayKey, 0.0) != 0.0) SetBellHeadacheEndDay(0.0);
        }

        // Per-tick upkeep: extends an active headache while exposed; once it has run out, clears it and
        // sends the end message if the start message was sent this session.
        public void TickBellHeadache(bool exposed)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            if (IsBellHeadacheActive(entity))
            {
                if (exposed) SetBellHeadacheEndDay(entity.World.Calendar.TotalDays + BellHeadacheLingerDays);
                return;
            }

            if (entity.WatchedAttributes.GetDouble(BellHeadacheEndDayKey, 0.0) == 0.0) return;

            bool announce = bellHeadacheAnnounced;
            ClearBellHeadache();
            if (announce) SendBellMessage(Lang.Get("remedyandruin:bones-bell-headache-ended"));
        }

        private void SetBellHeadacheEndDay(double endDay)
        {
            entity.WatchedAttributes.SetDouble(BellHeadacheEndDayKey, endDay);
            entity.WatchedAttributes.MarkPathDirty(BellHeadacheEndDayKey);
        }

        public void ResumeAfterLogin(EntityAgent target)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            bellHeadacheAnnounced = false;
            if (!IsBellHeadacheActive(entity)) ClearBellHeadache();
            else bellHeadacheAnnounced = true;

            foreach (BoneLocation limb in Enum.GetValues(typeof(BoneLocation)))
            {
                if (CompleteIfExpired(limb)) continue;
                RefreshHealing(limb);
            }
            UpdateTickListener();
            ApplyLegEffects();
            ApplyArmEffects();
            ApplySkullEffects();
            EnforceOffhand();
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
            float chance = DebugUseChancePercent.HasValue ? DebugUseChancePercent.Value / 100f : UseEscalationChance;
            if (entity.World.Rand.NextDouble() >= chance) return false;

            return Raise(limb, 1);
        }

        // Applies the who-may-roll policy for a use event, then rolls RollUseEscalation on the limb the
        // event stresses (ChestArms for ArmUse, Legs otherwise). Nobody rolls in creative or spectator
        // mode or with an uninjured limb, and a mounted player's own legs do not roll (the mount
        // carries them). A splinted limb stops rolling from ArmUse and Footstep; Jump and HardLanding
        // roll for any injured leg, splinted or not. Server only.
        public bool RollFromUse(UseKind kind)
        {
            if (entity.World.Side != EnumAppSide.Server || !entity.Alive) return false;
            if (IsCreativeOrSpectator(entity)) return false;
            if (kind != UseKind.ArmUse && (entity as EntityPlayer)?.MountedOn != null) return false;

            BoneLocation limb = kind == UseKind.ArmUse ? BoneLocation.ChestArms : BoneLocation.Legs;
            if (GetTier(limb) == InjuryTier.None) return false;
            if ((kind == UseKind.ArmUse || kind == UseKind.Footstep) && IsSplinted(limb)) return false;

            return RollUseEscalation(limb);
        }

        private static bool IsCreativeOrSpectator(Entity entity)
        {
            EnumGameMode? mode = (entity as EntityPlayer)?.Player?.WorldData?.CurrentGameMode;
            return mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator;
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
            ClearBellHeadache();
            UpdateTickListener();
            ApplyLegEffects();
            ApplyArmEffects();
        }

        public static void HandlePostArmorDamage(EntityPlayer victim, DamageSource source, ref float damage)
        {
            HitClassification? hit = HitContext.Current;
            if (hit == null || !hit.Value.CanInjure) return;

            EntityBehaviorBrokenBones bones = victim.GetBehavior<EntityBehaviorBrokenBones>();
            if (bones == null) return;

            EscalateFromHit(bones, victim, hit.Value, ref damage);

            // Fall damage is a hard landing: an extra use roll on top of the hit roll, splinted or not.
            if (source?.Source == EnumDamageSource.Fall && hit.Value.Location == BoneLocation.Legs)
            {
                bones.RollFromUse(UseKind.HardLanding);
            }
        }

        private static void EscalateFromHit(EntityBehaviorBrokenBones bones, EntityPlayer victim, HitClassification hit, ref float damage)
        {

            // The jump size is decided by post-armor damage alone for every damage type; revisit when
            // enemies can deal several damage types in one hit.
            BoneLocation limb = hit.Location;
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
            if (after >= InjuryTier.Fracture) entity.World.PlaySoundAt(BoneBreakSound, entity);
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

            SetTier(limb, InjuryTier.None);
            SetSplinted(limb, false);
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
            ApplyArmEffects();
            EnforceOffhand();
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

        private void SendMessage(BoneLocation limb, string text) => SendToPlayer("remedyandruinBones" + LimbName(limb), text);

        private void SendBellMessage(string text) => SendToPlayer("remedyandruinBellHeadache", text);

        private void SendToPlayer(string toastCode, string text)
        {
            if (entity is not EntityPlayer player || player.Player is not IServerPlayer serverPlayer) return;

            serverPlayer.SendIngameError(toastCode, text);
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

        private static InjuryTier ReadTier(Entity entity, BoneLocation limb) =>
            (InjuryTier)entity.WatchedAttributes.GetInt(TierKey(limb), (int)InjuryTier.None);

        private static bool ReadSplinted(Entity entity, BoneLocation limb) =>
            entity.WatchedAttributes.GetBool(SplintedKey(limb), false);

        private static string TierKey(BoneLocation limb) => "remedyandruinBones" + limb + "Tier";

        private static string EndDayKey(BoneLocation limb) => "remedyandruinBones" + limb + "HealEndDay";

        private static string SplintedKey(BoneLocation limb) => "remedyandruinBones" + limb + "Splinted";
    }
}
