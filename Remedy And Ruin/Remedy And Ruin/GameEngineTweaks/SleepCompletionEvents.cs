using Vintagestory.API.Common;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Fires once a player unmounts a bed, server-side only. wokeNaturally is true when Tiredness
    /// had reached 0 (a full, uninterrupted sleep completed) and false for any early exit (sneak-key
    /// dismount, bed destroyed, temporal storm interruption) - see BlockEntityBed.RestPlayer/
    /// DidUnmount for the exact conditions this mirrors. sedativeAssisted is true only if
    /// SedativeAssistedSleepAttributeKey was set on the entity before this sleep started: a valid
    /// Sedative drink sets it as it raises Tiredness above the sleep gate, and it is cleared here,
    /// or by the remedy behavior once the dose has expired with no sleep in progress. A listener
    /// can use it to tell an ordinary night's sleep apart from a Sedative-induced one.
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
