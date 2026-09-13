using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks.Hallucination
{
    /// <summary>
    /// One family's AI flow (wander/beeline/attack decisions), driving its own
    /// ClientEntityAILib.ClientControlledEntity. Each implementation owns its entity's full
    /// lifecycle, so HallucinationManager only ever needs Tick/RequestedDespawn/Dispose.
    /// </summary>
    internal interface IApparitionBehavior
    {
        void Tick(float dt, Vec3d playerPos);

        /// <summary>Set once this behavior has decided it's done (e.g. finished its attack) and wants HallucinationManager to despawn it.</summary>
        bool RequestedDespawn { get; }

        void Dispose();
    }
}
