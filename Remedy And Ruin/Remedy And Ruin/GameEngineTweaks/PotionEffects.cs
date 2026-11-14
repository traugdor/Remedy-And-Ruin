using System;
using Remedy_And_Ruin.RRBehaviors;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    public enum PotionLevel
    {
        None,
        Regular,
        Concentrated
    }

    /// <summary>
    /// Read side of the remedy potions. Each valid dose is a timed record in the entity's
    /// remedyandruinEffects/rrpotions WatchedAttributes list (written by
    /// EntityBehaviorRemedyEffects.ApplyEffect, removed by EffectThreadManager when its 8
    /// in-game hours end), so every query here works on the server and on a client that receives
    /// the synced attribute. Diluted potions never write a record.
    /// </summary>
    public static class PotionEffects
    {
        /// <summary>Length of a remedy dose in in-game hours, except the Sedative's.</summary>
        public const double DoseDurationHours = 8.0;

        /// <summary>
        /// Length of a Sedative dose. Long enough to cover the longest bed sleep from full
        /// Tiredness (8.5 hours in an aged-wood bed).
        /// </summary>
        public const double SedativeDoseDurationHours = 9.0;

        /// <summary>Dose length for a remedy cluster, regular and Concentrated alike.</summary>
        public static double DoseDurationFor(EntityBehaviorRemedyEffects.EffectCluster cluster)
        {
            return cluster == EntityBehaviorRemedyEffects.EffectCluster.SEDATIVE
                ? SedativeDoseDurationHours
                : DoseDurationHours;
        }

        /// <summary>Litres in one remedy dose; a drink of N times this many litres counts as N doses.</summary>
        public const float LitresPerDose = 0.25f;

        /// <summary>Record flag set on a potion's dose record while that potion is overdosed.</summary>
        public const string OverdosedKey = "overdosed";

        /// <summary>True while the cluster has an unexpired dose record flagged overdosed, which lasts until the original dose's window ends.</summary>
        public static bool IsOverdosed(Entity entity, EntityBehaviorRemedyEffects.EffectCluster cluster)
        {
            foreach (TreeAttribute record in ActiveRecords(entity, cluster))
            {
                if (record.GetBool(OverdosedKey)) return true;
            }
            return false;
        }

        /// <summary>Factor on the incoming Chest Cold contagion chance while a Tonic dose is active.</summary>
        public const double TonicContagionFactor = 0.5;

        /// <summary>Factor on the incoming Chest Cold contagion chance while the Antiviral is overdosed.</summary>
        public const double AntiviralOverdoseContagionFactor = 1.5;

        /// <summary>
        /// Multiplier the remedy potions apply to the entity's incoming Chest Cold contagion chance:
        /// 0.5 while a Tonic dose is unexpired, times 1.5 while the Antiviral is overdosed (0.75 with
        /// both). Derived from the dose records on every call, so it ends with the dose window and
        /// needs no stat to clear on death, relog or sleep.
        /// </summary>
        public static double ContagionChanceMultiplier(Entity entity)
        {
            double multiplier = 1.0;
            if (ActivePotionLevel(entity, EntityBehaviorRemedyEffects.EffectCluster.TONIC) != PotionLevel.None)
            {
                multiplier *= TonicContagionFactor;
            }
            if (IsOverdosed(entity, EntityBehaviorRemedyEffects.EffectCluster.ANTIVIRAL))
            {
                multiplier *= AntiviralOverdoseContagionFactor;
            }
            return multiplier;
        }

        /// <summary>Factor on a pain penalty while any Analgesic dose is active.</summary>
        public const float AnalgesicPainFactor = 0.5f;

        /// <summary>
        /// Multiplier for pain penalties that an Analgesic only softens: the broken-bone walk and
        /// work-speed penalties. 0.5 while an Analgesic dose of either tier is unexpired, otherwise
        /// 1. Never reaches 0, so the Concentrated tier softens no further than the regular one.
        /// Derived from the dose records on every call.
        /// </summary>
        public static float PainScale(Entity entity)
        {
            return ActivePotionLevel(entity, EntityBehaviorRemedyEffects.EffectCluster.ANALGESIC) != PotionLevel.None
                ? AnalgesicPainFactor
                : 1f;
        }

        /// <summary>
        /// Multiplier for the walk-speed penalty of Wound Infection and Flu's fever: 1 with no
        /// unexpired Analgesic dose, 0.5 with a regular dose, 0 with a Concentrated dose, which
        /// clears the penalty entirely. Derived from the dose records on every call.
        /// </summary>
        public static float WalkPenaltyScale(Entity entity)
        {
            switch (ActivePotionLevel(entity, EntityBehaviorRemedyEffects.EffectCluster.ANALGESIC))
            {
                case PotionLevel.Regular: return AnalgesicPainFactor;
                case PotionLevel.Concentrated: return 0f;
                default: return 1f;
            }
        }

        /// <summary>True while an Analgesic dose of either tier is unexpired.</summary>
        public static bool HasAnalgesic(Entity entity)
        {
            return ActivePotionLevel(entity, EntityBehaviorRemedyEffects.EffectCluster.ANALGESIC) != PotionLevel.None;
        }

        /// <summary>A potion item whose effectMultiplier is at least this is the Concentrated tier.</summary>
        public const float ConcentratedEffectMultiplier = 2.0f;

        /// <summary>True for the eight remedy potion clusters. The Antidote and every poison are excluded.</summary>
        public static bool IsRemedyCluster(EntityBehaviorRemedyEffects.EffectCluster cluster)
        {
            switch (cluster)
            {
                case EntityBehaviorRemedyEffects.EffectCluster.ANALGESIC:
                case EntityBehaviorRemedyEffects.EffectCluster.ANTINAUSEA:
                case EntityBehaviorRemedyEffects.EffectCluster.ANTISEPTIC:
                case EntityBehaviorRemedyEffects.EffectCluster.ANTIVIRAL:
                case EntityBehaviorRemedyEffects.EffectCluster.MINDTONIC:
                case EntityBehaviorRemedyEffects.EffectCluster.SEDATIVE:
                case EntityBehaviorRemedyEffects.EffectCluster.TONIC:
                case EntityBehaviorRemedyEffects.EffectCluster.TOPICALOINTMENT:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Maps a potion item's effectMultiplier to its tier: 0 is Diluted (None), 1 Regular, 2 or more Concentrated.</summary>
        public static PotionLevel LevelFromEffectMultiplier(float effectMultiplier)
        {
            if (effectMultiplier <= 0f) return PotionLevel.None;
            return effectMultiplier >= ConcentratedEffectMultiplier ? PotionLevel.Concentrated : PotionLevel.Regular;
        }

        /// <summary>
        /// The strongest unexpired dose record of the given remedy cluster on the entity, or None.
        /// </summary>
        public static PotionLevel ActivePotionLevel(Entity entity, EntityBehaviorRemedyEffects.EffectCluster cluster)
        {
            PotionLevel strongest = PotionLevel.None;
            foreach (TreeAttribute record in ActiveRecords(entity, cluster))
            {
                PotionLevel level = LevelOfRecord(record);
                if (level > strongest) strongest = level;
            }
            return strongest;
        }

        /// <summary>The tier a stored dose record was written at.</summary>
        public static PotionLevel LevelOfRecord(TreeAttribute record)
        {
            return LevelFromEffectMultiplier(record.GetFloat("effectMultiplier"));
        }

        /// <summary>
        /// Every unexpired record in the entity's rrpotions list for the cluster. A record carrying
        /// an absolute end hour that the calendar has already passed is treated as gone even if its
        /// timer has not yet removed it.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<TreeAttribute> ActiveRecords(Entity entity, EntityBehaviorRemedyEffects.EffectCluster cluster)
        {
            var potions = entity?.WatchedAttributes?.GetTreeAttribute("remedyandruinEffects")?["rrpotions"] as TreeArrayAttribute;
            if (potions?.value == null) yield break;

            string clusterName = cluster.ToString();
            double totalHours = entity.World.Calendar.TotalHours;
            foreach (TreeAttribute record in potions.value)
            {
                if (record.GetString("cluster") != clusterName) continue;
                if (record.HasAttribute("absoluteEndTotalHours") && totalHours >= record.GetDouble("absoluteEndTotalHours")) continue;
                yield return record;
            }
        }
    }
}
