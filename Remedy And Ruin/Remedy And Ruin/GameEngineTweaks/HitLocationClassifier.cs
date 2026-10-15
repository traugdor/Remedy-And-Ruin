using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    public enum BoneLocation
    {
        Skull,
        ChestArms,
        Legs
    }

    /// <summary>Where a hit landed and whether it may roll a bone injury (false for hail, which only picks the armor slot).</summary>
    public readonly struct HitClassification
    {
        public BoneLocation Location { get; }
        public bool CanInjure { get; }

        public HitClassification(BoneLocation location, bool canInjure)
        {
            Location = location;
            CanInjure = canInjure;
        }
    }

    /// <summary>
    /// Server-side mapping from a damaging hit on a player to a body location. Rules apply in this order:
    /// <list type="number">
    /// <item>Never rolls (null): non-physical damage types, Bleed/Internal/Void/Drown/Suicide/Revive sources,
    /// and damage converted to a DoT (Duration &gt; 0). Lightning is covered by its Electricity type.
    /// Hail (Weather + BluntAttack) returns Skull with CanInjure false.</item>
    /// <item>Fixed locations: Fall and eidolon ground slam, sharp-block touch and saddle-break throw give Legs;
    /// a falling block gives Skull; explosions pick one of the three locations uniformly.</item>
    /// <item>Player melee: DamageSource.HitPosition is relative to the victim's feet and is set only by
    /// player melee (lightning sets a zero vector, which is ignored); its Y is the exact hit height.</item>
    /// <item>Per-attacker fixed locations by cause-entity code (see <see cref="TryGetFixedLocation"/>).</item>
    /// <item>Estimated height: for projectiles and thrown objects the projectile's estimated impact height, otherwise 60% up the
    /// attacker's selection box, relative to the victim's feet and clamped to the victim's current box.
    /// SourcePos is used when there is no attacker.</item>
    /// <item>Nothing usable gives ChestArms.</item>
    /// </list>
    /// Height cutoffs are fractions of the victim's current SelectionBox.Y2 (updated server-side from the
    /// player's sneak and sit state), so a crouched player still has a head.
    /// </summary>
    public static class HitLocationClassifier
    {
        private const double StandingHeight = 1.85;
        private const double LegsCutoffStanding = 0.9;
        private const double SkullCutoffStanding = 1.5;
        private const double CoinFlipBandStanding = 0.005;
        private const double AttackerHitFraction = 0.6;

        public static HitClassification? Classify(EntityPlayer victim, DamageSource source)
        {
            if (NeverRolls(source)) return null;

            if (source.Source == EnumDamageSource.Weather && source.Type == EnumDamageType.BluntAttack)
            {
                return new HitClassification(BoneLocation.Skull, false);
            }

            Random rand = victim.World.Rand;

            if (source.Source == EnumDamageSource.Fall) return Injure(BoneLocation.Legs);
            if (source.Source == EnumDamageSource.Explosion) return Injure(RandomLocation(rand));
            if (source.Source == EnumDamageSource.Block)
            {
                if (source.Type == EnumDamageType.Crushing) return Injure(BoneLocation.Skull);
                if (source.SourceEntity == null && IsPhysical(source.Type)) return Injure(BoneLocation.Legs);
            }
            if (IsEidolonSlam(source) || IsSaddleBreakThrow(victim, source)) return Injure(BoneLocation.Legs);

            double victimHeight = victim.SelectionBox.Y2;

            Vec3d hit = source.HitPosition;
            if (hit != null && (hit.X != 0 || hit.Y != 0 || hit.Z != 0))
            {
                return Injure(ClassifyHeight(Clamp(hit.Y, victimHeight), victimHeight, rand));
            }

            Entity attacker = source.GetCauseEntity();
            if (attacker != null && TryGetFixedLocation(attacker.Code?.Path, IsMelee(source), rand, out BoneLocation fixedLocation))
            {
                return Injure(fixedLocation);
            }

            if (TryEstimateHeight(victim, source, attacker, out double height))
            {
                return Injure(ClassifyHeight(Clamp(height, victimHeight), victimHeight, rand));
            }

            return Injure(BoneLocation.ChestArms);
        }

        /// <summary>
        /// Per-attacker fixed locations keyed on the entity code path: polar bear (Chest/Arms or Skull),
        /// bowtorn and Erel (Skull), bee (Legs or Chest/Arms) and the eidolon's 360-degree sweep (Chest/Arms).
        /// The eidolon entry applies to melee only; its thrown boulders use the height estimate.
        /// </summary>
        public static bool TryGetFixedLocation(string codePath, bool melee, Random rand, out BoneLocation location)
        {
            location = BoneLocation.ChestArms;
            if (string.IsNullOrEmpty(codePath)) return false;

            if (codePath.StartsWith("bear-polar-", StringComparison.Ordinal))
            {
                location = rand.Next(2) == 0 ? BoneLocation.ChestArms : BoneLocation.Skull;
                return true;
            }
            if (CodeIs(codePath, "bowtorn") || CodeIs(codePath, "erel"))
            {
                location = BoneLocation.Skull;
                return true;
            }
            if (CodeIs(codePath, "beemob"))
            {
                location = rand.Next(2) == 0 ? BoneLocation.Legs : BoneLocation.ChestArms;
                return true;
            }
            if (melee && CodeIs(codePath, "eidolon"))
            {
                location = BoneLocation.ChestArms;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Maps a height above the victim's feet to a location. Cutoffs scale with victimHeight
        /// (0.9 and 1.5 at a standing 1.85); within a narrow band around a cutoff the neighbours are a coin flip.
        /// </summary>
        public static BoneLocation ClassifyHeight(double height, double victimHeight, Random rand)
        {
            GetCutoffs(victimHeight, out double legsCutoff, out double skullCutoff, out double band);

            if (Math.Abs(height - legsCutoff) <= band)
            {
                return rand.Next(2) == 0 ? BoneLocation.Legs : BoneLocation.ChestArms;
            }
            if (Math.Abs(height - skullCutoff) <= band)
            {
                return rand.Next(2) == 0 ? BoneLocation.ChestArms : BoneLocation.Skull;
            }
            if (height < legsCutoff) return BoneLocation.Legs;
            if (height > skullCutoff) return BoneLocation.Skull;
            return BoneLocation.ChestArms;
        }

        public static void GetCutoffs(double victimHeight, out double legsCutoff, out double skullCutoff, out double coinFlipBand)
        {
            double scale = victimHeight / StandingHeight;
            legsCutoff = LegsCutoffStanding * scale;
            skullCutoff = SkullCutoffStanding * scale;
            coinFlipBand = CoinFlipBandStanding * scale;
        }

        /// <summary>Estimated hit height for a melee attacker of the given selection-box height standing level with the victim.</summary>
        public static double EstimateAttackerHeight(double attackerBoxHeight) => AttackerHitFraction * attackerBoxHeight;

        private static bool TryEstimateHeight(EntityPlayer victim, DamageSource source, Entity attacker, out double height)
        {
            height = 0;
            if (source.SourceEntity is IProjectile)
            {
                height = ProjectileImpactHeight.Estimate(victim, source.SourceEntity);
                return true;
            }
            if (attacker != null)
            {
                height = attacker.Pos.Y + EstimateAttackerHeight(attacker.SelectionBox?.Y2 ?? 0) - victim.Pos.Y;
                return true;
            }
            if (source.SourcePos != null)
            {
                height = source.SourcePos.Y - victim.Pos.Y;
                return true;
            }
            return false;
        }

        private static bool NeverRolls(DamageSource source)
        {
            if (source.Duration > TimeSpan.Zero) return true;

            switch (source.Source)
            {
                case EnumDamageSource.Bleed:
                case EnumDamageSource.Internal:
                case EnumDamageSource.Void:
                case EnumDamageSource.Drown:
                case EnumDamageSource.Suicide:
                case EnumDamageSource.Revive:
                    return true;
            }

            switch (source.Type)
            {
                case EnumDamageType.Fire:
                case EnumDamageType.Poison:
                case EnumDamageType.Hunger:
                case EnumDamageType.Frost:
                case EnumDamageType.Electricity:
                case EnumDamageType.Heat:
                case EnumDamageType.Acid:
                case EnumDamageType.Suffocation:
                case EnumDamageType.Injury:
                case EnumDamageType.Heal:
                    return true;
            }
            return false;
        }

        private static bool IsPhysical(EnumDamageType type) =>
            type == EnumDamageType.BluntAttack || type == EnumDamageType.SlashingAttack || type == EnumDamageType.PiercingAttack;

        private static bool IsMelee(DamageSource source) =>
            source.SourceEntity == null || source.CauseEntity == null || ReferenceEquals(source.SourceEntity, source.CauseEntity);

        /// <summary>The eidolon ground slam is the only eidolon damage with BluntAttack, zero knockback and no cause entity (the sweep slashes with knockback).</summary>
        private static bool IsEidolonSlam(DamageSource source) =>
            source.Source == EnumDamageSource.Entity
            && source.Type == EnumDamageType.BluntAttack
            && source.KnockbackStrength == 0f
            && source.CauseEntity == null
            && source.SourceEntity?.Code != null
            && CodeIs(source.SourceEntity.Code.Path, "eidolon");

        /// <summary>
        /// DoSaddleBreak damages the rider with only CauseEntity set (the mount) while the rider is still
        /// seated on it; melee and projectile damage always carry a SourceEntity.
        /// </summary>
        private static bool IsSaddleBreakThrow(EntityPlayer victim, DamageSource source) =>
            source.Source == EnumDamageSource.Entity
            && source.SourceEntity == null
            && source.CauseEntity != null
            && victim.MountedOn?.Entity != null
            && ReferenceEquals(victim.MountedOn.Entity, source.CauseEntity)
            && source.CauseEntity.HasBehavior<EntityBehaviorRideable>();

        private static bool CodeIs(string path, string prefix) =>
            path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "-", StringComparison.Ordinal);

        private static BoneLocation RandomLocation(Random rand) => (BoneLocation)rand.Next(3);

        private static double Clamp(double height, double victimHeight) => Math.Max(0, Math.Min(height, victimHeight));

        private static HitClassification Injure(BoneLocation location) => new HitClassification(location, true);
    }
}
