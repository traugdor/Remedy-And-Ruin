using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Ordered chain of server-side handlers that see damage to a player after armor has been applied.
    /// Armor is an OnDamaged delegate invoked from EntityBehaviorHealth.ApplyOnDamageDelegates, which
    /// runs inside OnEntityReceiveDamage, so a Prefix on OnEntityReceiveDamage only sees raw damage.
    /// The postfix here runs right after the delegates and receives the caller's own damage local by
    /// ref, so a value a handler writes is what vanilla then subtracts from health.
    ///
    /// Handlers run in registration order, only for EntityPlayer victims, non-Heal damage and
    /// damage &gt; 0. The chain stops early once the damage reaches 0 or a handler marks the hit
    /// <see cref="HitContext.Lethal"/>. Damage converted into a DoT (DamageSource.Duration &gt; 0) and DoT ticks never
    /// reach ApplyOnDamageDelegates, so handlers never see them. Register once per server start and
    /// call Clear when the server shuts down.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), "ApplyOnDamageDelegates")]
    public static class PostArmorDamagePipeline
    {
        public delegate void Handler(EntityPlayer victim, DamageSource source, ref float damage);

        private static readonly List<Handler> handlers = new List<Handler>();

        public static void Register(Handler handler) => handlers.Add(handler);

        public static void Clear() => handlers.Clear();

        public static void Run(EntityPlayer victim, DamageSource source, ref float damage)
        {
            foreach (Handler handler in handlers)
            {
                handler(victim, source, ref damage);
                if (damage <= 0f || HitContext.Lethal) return;
            }
        }

        public static void Postfix(EntityBehaviorHealth __instance, DamageSource damageSource, ref float damage)
        {
            if (__instance.entity.World.Side != EnumAppSide.Server) return;
            if (__instance.entity is not EntityPlayer victim) return;
            if (damageSource.Type == EnumDamageType.Heal || damage <= 0f) return;

            Run(victim, damageSource, ref damage);
        }
    }
}
