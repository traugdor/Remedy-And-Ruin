using System.IO;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Generates tinted liquid textures at runtime instead of hand-drawing one per
    /// remedy/poison cluster. One neutral base liquid texture gets its RGB channels
    /// multiplied by a per-cluster color (and alpha scaled per tier - Diluted vs. Potion
    /// vs. Concentrated) and the result is inserted into the block texture atlas under a
    /// made-up AssetLocation used purely as a cache key. Adding a new cluster is then
    /// just a new color entry, never new art.
    /// </summary>
    public static class LiquidTintTextureSystem
    {
        /// <summary>
        /// Converts every pixel of <paramref name="baseBmp"/> to grayscale luminance first,
        /// then multiplies that luminance by the given tint color (and scales alpha),
        /// returning a new bitmap the same size as the source. Does not modify baseBmp.
        /// Desaturating first matters: multiplying the tint directly against the base
        /// texture's own (un-gray) pixels lets whatever hue the base already has (water's
        /// blue, for instance) bleed through and skew the result away from the intended
        /// color. Converting to luminance first means the base texture only ever contributes
        /// brightness/ripple detail - the tint color is the sole source of hue.
        /// </summary>
        public static BitmapExternal GenerateTintedBitmap(BitmapRef baseBmp, int tintR, int tintG, int tintB, float alphaMultiplier)
        {
            int width = baseBmp.Width;
            int height = baseBmp.Height;
            BitmapExternal result = new BitmapExternal(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    SKColor src = baseBmp.GetPixel(x, y);
                    float luminance = (0.299f * src.Red + 0.587f * src.Green + 0.114f * src.Blue) / 255f;
                    byte r = (byte)GameMath.Clamp(luminance * tintR, 0f, 255f);
                    byte g = (byte)GameMath.Clamp(luminance * tintG, 0f, 255f);
                    byte b = (byte)GameMath.Clamp(luminance * tintB, 0f, 255f);
                    byte a = (byte)GameMath.Clamp(src.Alpha * alphaMultiplier, 0f, 255f);
                    result.bmp.SetPixel(x, y, new SKColor(r, g, b, a));
                }
            }

            return result;
        }

        /// <summary>
        /// Generates (or returns the already-cached) tinted variant of baseBmp for the given
        /// cache key, inserting it into the block texture atlas. cacheKey should be unique per
        /// cluster+tier combination (e.g. "remedyandruin:generated/liquid-antiseptic-potion").
        /// </summary>
        public static bool GetOrCreateTintedTexture(ICoreClientAPI capi, AssetLocation cacheKey, BitmapRef baseBmp, int tintR, int tintG, int tintB, float alphaMultiplier, out int textureSubId, out TextureAtlasPosition texPos)
        {
            return capi.BlockTextureAtlas.GetOrInsertTexture(cacheKey, out textureSubId, out texPos, () => GenerateTintedBitmap(baseBmp, tintR, tintG, tintB, alphaMultiplier));
        }

        /// <summary>
        /// Debug-only: generates a tinted bitmap and saves it straight to a PNG file, so the
        /// tint math can be checked visually without needing the atlas/shape-tesselation
        /// pipeline wired up yet.
        /// </summary>
        public static void SaveTintedBitmapForTesting(BitmapRef baseBmp, int tintR, int tintG, int tintB, float alphaMultiplier, string outputPath)
        {
            using BitmapExternal tinted = GenerateTintedBitmap(baseBmp, tintR, tintG, tintB, alphaMultiplier);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            tinted.Save(outputPath);
        }
    }
}
