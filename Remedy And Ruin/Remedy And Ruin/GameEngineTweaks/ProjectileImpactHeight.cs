using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Estimates the height above a victim's feet at which a projectile struck. Vanilla sets
    /// DamageSource.HitPosition only for player melee. Used only when HitPosition is absent. If vanilla
    /// begins supplying HitPosition for projectiles this class is unused and can be deleted together with
    /// its single call in HitLocationClassifier.
    ///
    /// The projectile entity still has its Pos and Pos.Motion while the victim receives damage, but its
    /// hit test stretches its box along the motion, so Pos can sit short of the real contact point. The
    /// estimate casts a ray from Pos along Motion into the victim's collision box and uses the entry
    /// point; with no motion or a missed ray it uses Pos.Y clamped into the box. The player's collision
    /// and selection boxes are resized identically, so the result is directly on the classifier's scale.
    /// </summary>
    public static class ProjectileImpactHeight
    {
        private const double MinMotion = 1e-6;

        public static double Estimate(EntityPlayer victim, Entity projectile)
        {
            Cuboidd box = new Cuboidd().SetAndTranslate(victim.CollisionBox, victim.Pos.X, victim.Pos.Y, victim.Pos.Z);
            return Estimate(box, victim.Pos.Y, projectile.Pos.XYZ, projectile.Pos.Motion);
        }

        /// <summary>Returns the entry height of the ray minus feetY, or the clamped origin height when there is no entry.</summary>
        public static double Estimate(Cuboidd victimBox, double feetY, Vec3d projectilePos, Vec3d projectileMotion)
        {
            double entryY = Math.Max(victimBox.MinY, Math.Min(projectilePos.Y, victimBox.MaxY));

            double length = projectileMotion.Length();
            if (length > MinMotion)
            {
                double tEnter = 0;
                double tExit = double.PositiveInfinity;
                if (Slab(projectilePos.X, projectileMotion.X / length, victimBox.MinX, victimBox.MaxX, ref tEnter, ref tExit)
                    && Slab(projectilePos.Y, projectileMotion.Y / length, victimBox.MinY, victimBox.MaxY, ref tEnter, ref tExit)
                    && Slab(projectilePos.Z, projectileMotion.Z / length, victimBox.MinZ, victimBox.MaxZ, ref tEnter, ref tExit))
                {
                    entryY = projectilePos.Y + projectileMotion.Y / length * tEnter;
                }
            }

            return entryY - feetY;
        }

        private static bool Slab(double origin, double direction, double min, double max, ref double tEnter, ref double tExit)
        {
            if (Math.Abs(direction) < 1e-12) return origin >= min && origin <= max;

            double t1 = (min - origin) / direction;
            double t2 = (max - origin) / direction;
            if (t1 > t2) (t1, t2) = (t2, t1);

            tEnter = Math.Max(tEnter, t1);
            tExit = Math.Min(tExit, t2);
            return tEnter <= tExit;
        }
    }
}
