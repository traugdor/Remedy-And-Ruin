namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Per-hit state for the damage currently being applied to a player on the server thread. Patch_HitContext
    /// sets <see cref="Current"/> before vanilla armor runs and clears it after every post-armor handler has run,
    /// so armor selection and bone injuries share one classification (and its random choices).
    /// </summary>
    public static class HitContext
    {
        [System.ThreadStatic]
        private static HitClassification? current;

        [System.ThreadStatic]
        private static BoneLocation? forcedLocation;

        /// <summary>The classification of the hit being applied, or null outside a hit or for damage the classifier ignores.</summary>
        public static HitClassification? Current
        {
            get => current;
            set => current = value;
        }

        /// <summary>Debug: when set, every hit on a player is treated as landing at this location and may injure.</summary>
        public static BoneLocation? ForcedLocation
        {
            get => forcedLocation;
            set => forcedLocation = value;
        }
    }
}
