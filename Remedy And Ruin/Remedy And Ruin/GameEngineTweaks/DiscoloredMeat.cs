using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Shared definitions for discolored meat: the six item codes the JSON patches add to the
    /// vanilla redmeat and bushmeat items (a raw tier that drops from animals killed by Toxic
    /// arrow poison, which cooks into the cooked tier or cures into the cured tier), the predicate that recognises them, and the
    /// runtime generation of their tinted textures.
    /// </summary>
    public static class DiscoloredMeat
    {
        public const string RedMeatRawTierPath = "redmeat-discolored";
        public const string RedMeatCookedTierPath = "redmeat-cookeddiscolored";
        public const string BushMeatRawTierPath = "bushmeat-discolored";
        public const string BushMeatCookedTierPath = "bushmeat-cookeddiscolored";
        public const string RedMeatCuredTierPath = "redmeat-cureddiscolored";
        public const string BushMeatCuredTierPath = "bushmeat-cureddiscolored";

        // Item texture paths (asset domain remedyandruin) the item patches point at.
        public const string RawTextureFileName = "meat_discolored_raw_generated";
        public const string CookedTextureFileName = "meat_discolored_cooked_generated";
        public const string CuredTextureFileName = "meat_discolored_cured_generated";

        // Tinted copies of the vanilla cooking pot meal palette images that carry the red meat
        // pixels (soup / stew shapes); block textures `group2discolored` and `group6discolored`
        // in the pot patch point at them.
        private const string MealGroup2SourceFileName = "group2-soup-flyagaric-cactifruit-redmeat";
        private const string MealGroup6SourceFileName = "group6-spelt-onion-redcurrant-egg-redmeatstew";
        public const string MealGroup2TextureFileName = "group2-discolored_generated";
        public const string MealGroup6TextureFileName = "group6-discolored_generated";

        // Green-grey: the vanilla meat texture's luminance multiplied by this color.
        private const byte TintR = 190;
        private const byte TintG = 215;
        private const byte TintB = 165;

        /// <summary>True for any of the four discolored meat items, raw or cooked tier.</summary>
        public static bool IsDiscolored(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            if (code == null || code.Domain != "game") return false;

            string path = code.Path;
            return path == RedMeatRawTierPath || path == RedMeatCookedTierPath
                || path == BushMeatRawTierPath || path == BushMeatCookedTierPath
                || path == RedMeatCuredTierPath || path == BushMeatCuredTierPath;
        }

        /// <summary>
        /// Writes the tinted raw, cooked and cured meat textures and the tinted meal group images under the generated asset root
        /// (RemedyPoisonClusterTextures.GetAssetRootPath) unless they already exist, using the
        /// same luminance-times-tint generator as the cluster liquid textures. Call from mod
        /// Start() on the client before the asset origin is registered; reads the vanilla textures
        /// straight from disk because the asset system is not readable that early. Returns false
        /// when a source texture is missing.
        /// </summary>
        public static bool EnsureGeneratedTextures(ICoreAPI api)
        {
            string survivalTextures = Path.Combine(GamePaths.AssetsPath, "survival", "textures");
            string dir = Path.Combine(RemedyPoisonClusterTextures.GetAssetRootPath(api), "textures", "item", "food", "meat");
            Directory.CreateDirectory(dir);

            bool raw = EnsureTinted(Path.Combine(survivalTextures, "item", "food", "meat", "raw.png"), Path.Combine(dir, RawTextureFileName + ".png"));
            bool cooked = EnsureTinted(Path.Combine(survivalTextures, "item", "food", "meat", "cooked.png"), Path.Combine(dir, CookedTextureFileName + ".png"));
            bool cured = EnsureTinted(Path.Combine(survivalTextures, "item", "food", "meat", "cured.png"), Path.Combine(dir, CuredTextureFileName + ".png"));

            string mealSource = Path.Combine(survivalTextures, "block", "food", "meal");
            string mealDir = Path.Combine(RemedyPoisonClusterTextures.GetAssetRootPath(api), "textures", "block", "food", "meal");
            Directory.CreateDirectory(mealDir);
            bool group2 = EnsureTinted(Path.Combine(mealSource, MealGroup2SourceFileName + ".png"), Path.Combine(mealDir, MealGroup2TextureFileName + ".png"));
            bool group6 = EnsureTinted(Path.Combine(mealSource, MealGroup6SourceFileName + ".png"), Path.Combine(mealDir, MealGroup6TextureFileName + ".png"));
            return raw && cooked && cured && group2 && group6;
        }

        private static bool EnsureTinted(string sourcePath, string outputPath)
        {
            if (File.Exists(outputPath)) return true;
            if (!File.Exists(sourcePath)) return false;

            using BitmapExternal source = new BitmapExternal(sourcePath);
            using BitmapExternal tinted = LiquidTintTextureSystem.GenerateTintedBitmap(source, TintR, TintG, TintB, 1f);
            tinted.Save(outputPath);
            return true;
        }
    }
}
