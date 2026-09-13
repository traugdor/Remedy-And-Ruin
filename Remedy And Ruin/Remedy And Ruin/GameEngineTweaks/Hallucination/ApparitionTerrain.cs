using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks.Hallucination
{
    /// <summary>
    /// Finds solid ground near a known Y via a short local downward scan (IsSideSolid), the same
    /// technique vanilla's own AiTaskWander.MoveDownToFloor uses for drifters/shivers/bowtorns -
    /// not a worldgen or sky heightmap, which returns the topmost sky-facing surface (a cave roof
    /// or building roof, not the actual floor) whenever the position is underground or indoors.
    /// </summary>
    internal static class ApparitionTerrain
    {
        private const int MaxScanDown = 8;

        /// <summary>Returns the surface Y to stand on near (x, aroundY, z), or aroundY unchanged if nothing solid is found within range.</summary>
        internal static double FindGroundY(ICoreClientAPI capi, double x, double aroundY, double z)
        {
            int bx = (int)x, bz = (int)z;
            int y = (int)System.Math.Ceiling(aroundY) + 1;
            int tries = MaxScanDown;
            while (tries-- > 0)
            {
                if (capi.World.BlockAccessor.IsSideSolid(bx, y, bz, BlockFacing.UP)) return y + 1;
                y--;
            }
            return aroundY;
        }
    }
}
