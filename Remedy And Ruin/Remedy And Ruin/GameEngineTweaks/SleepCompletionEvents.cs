using Vintagestory.API.Common;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Fires once a player unmounts a bed, server-side only. wokeNaturally is true when Tiredness
    /// had reached 0 (a full, uninterrupted sleep completed) and false for any early exit (sneak-key
    /// dismount, bed destroyed, temporal storm interruption) - see BlockEntityBed.RestPlayer/
    /// DidUnmount for the exact conditions this mirrors. sedativeAssisted is true only if
    /// SedativeAssistedSleepAttributeKey was set on the entity before this sleep started - nothing
    /// sets that attribute yet (Sedative's own potion effect is a later plan), so it always reads
    /// false today; a future Sedative effect must set it at the moment it raises Tiredness above the
    /// sleep gate, so a listener can tell an ordinary night's sleep apart from a Sedative-induced one
    /// (e.g. before applying a "woke up dizzy" side effect that should only follow the latter).
    /// </summary>
    public static class SleepCompletionEvents
    {
        public const string SedativeAssistedSleepAttributeKey = "remedyandruinSedativeAssistedSleep";

        public delegate void SleepEndedDelegate(EntityAgent entity, bool wokeNaturally, bool sedativeAssisted);

        public static event SleepEndedDelegate OnSleepEnded;

        internal static void RaiseSleepEnded(EntityAgent entity, bool wokeNaturally)
        {
            bool sedativeAssisted = entity.WatchedAttributes.GetBool(SedativeAssistedSleepAttributeKey, false);
            entity.WatchedAttributes.RemoveAttribute(SedativeAssistedSleepAttributeKey);
            OnSleepEnded?.Invoke(entity, wokeNaturally, sedativeAssisted);
        }
    }
}
