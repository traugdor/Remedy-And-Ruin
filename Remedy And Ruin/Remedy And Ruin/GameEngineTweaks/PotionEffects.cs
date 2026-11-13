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
