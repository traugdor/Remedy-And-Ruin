using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Every remedy/poison cluster that needs a generated liquid texture, and the color each
    /// one tints to. Adding a new remedy or poison later is just a new enum value + a new
    /// entry in ClusterTints below - never new art.
    ///
    /// Situational poison deliberately has no entry here - per the design doc it reuses
    /// Gutbane's effect wholesale with no distinct identity of its own, so code needing
    /// its texture should just reference NoxiousPoison directly.
    /// </summary>
    public enum RemedyPoisonCluster
    {
        Antiseptic,
        Antinausea,
        Sedative,
        Antiviral,
        Analgesic,
        TopicalOintment,
        Tonic,
        MindTonic,
        ToxicPoison,
        NoxiousPoison,
        CardiacPoison,
        NeurotoxicPoison,
        MindPoison,
        Antidote
    }

    /// <summary>
    /// Which vanilla source texture a generated file is tinted from, and what it's for.
    /// Base genuinely reuses rot's own item shape ("item/rot" - the organic pile model, not a
    /// generic cube) per the settled design decision, which needs its own two texture slots
    /// tinted separately ("rot" -> rot-solids, "base" -> rot-spill) in addition to the flat
    /// waterTightContainerProps texture (from block/creature/rot/rot) used for how it looks
    /// scooped/contained.
    /// </summary>
    public enum RemedyTextureRole
    {
        Liquid,
        PotionBaseContainer,
        PotionBaseShapeSolids,
        PotionBaseShapeSpill
    }

    /// <summary>
    /// Looks up, generates, and registers the runtime-tinted liquid texture for each
    /// RemedyPoisonCluster. One generated PNG per cluster, saved under
    /// ModData/remedyandruin/&lt;cluster&gt;_generated.png at full opacity - Diluted Potion,
    /// Potion, and Concentrated Potion all share that one file and apply their own tier's
    /// opacity via their own item JSON's texture alpha (same mechanism vanilla's cider/spirit
    /// portions already use), rather than needing a separate generated file per tier.
    /// </summary>
    public static class RemedyPoisonClusterTextures
    {
        /// <summary>
        /// Body-system-paired where a real pairing exists (Antinausea/Noxious - green,
        /// Mind Tonic/Brain Rot - purple, Tonic/Heartbane - red), standalone
        /// otherwise. Poisons within a pair are the darker/muddier half.
        /// </summary>
        private static readonly Dictionary<RemedyPoisonCluster, (byte R, byte G, byte B)> ClusterTints = new Dictionary<RemedyPoisonCluster, (byte, byte, byte)>
        {
            { RemedyPoisonCluster.Antiseptic, (64, 180, 170) },
            { RemedyPoisonCluster.Antinausea, (140, 220, 120) },
            { RemedyPoisonCluster.Sedative, (60, 70, 160) },
            { RemedyPoisonCluster.Antiviral, (230, 190, 60) },
            { RemedyPoisonCluster.Analgesic, (200, 220, 235) },
            { RemedyPoisonCluster.TopicalOintment, (235, 180, 170) },
            { RemedyPoisonCluster.Tonic, (200, 40, 40) },
            { RemedyPoisonCluster.MindTonic, (150, 90, 210) },
            { RemedyPoisonCluster.ToxicPoison, (190, 200, 60) },
            { RemedyPoisonCluster.NoxiousPoison, (90, 120, 60) },
            { RemedyPoisonCluster.CardiacPoison, (110, 20, 25) },
            { RemedyPoisonCluster.NeurotoxicPoison, (110, 100, 120) },
            { RemedyPoisonCluster.MindPoison, (80, 50, 110) },
            { RemedyPoisonCluster.Antidote, (255, 245, 200) }
        };

        public static (byte R, byte G, byte B) GetClusterTint(RemedyPoisonCluster cluster)
        {
            return ClusterTints[cluster];
        }

        /// <summary>
        /// "&lt;cluster_with_no_spaces&gt;_generated.png" for the liquid tiers (Diluted
        /// Potion/Potion/Concentrated Potion, tinted from waterportion); for Base
        /// (which reuses rot's item wholesale, above) one file per rot texture it actually
        /// uses - "_base_generated" (container/waterTightContainerProps), "_base_solids_generated"
        /// and "_base_spill_generated" (the two texture slots item/rot's own shape references).
        /// The enum's own name is already space-free (e.g. TopicalOintment), just lowercased for
        /// a clean, case-consistent filename.
        /// </summary>
        public static string GetGeneratedFileName(RemedyPoisonCluster cluster, RemedyTextureRole role)
        {
            string suffix = role switch
            {
                RemedyTextureRole.Liquid => "_generated.png",
                RemedyTextureRole.PotionBaseContainer => "_base_generated.png",
                RemedyTextureRole.PotionBaseShapeSolids => "_base_solids_generated.png",
                RemedyTextureRole.PotionBaseShapeSpill => "_base_spill_generated.png",
                _ => "_generated.png"
            };
            return $"{cluster.ToString().ToLowerInvariant()}{suffix}";
        }

        /// <summary>
        /// Root folder registered with the asset system via ICoreAPI.Assets.AddModOrigin("remedyandruin", ...),
        /// so files written under here become resolvable as ordinary "remedyandruin:..." assets - item type
        /// JSON can reference the generated textures with a plain texture/base path, the same as any
        /// hand-drawn texture shipped in the mod's own assets folder.
        ///
        /// Timing is load-bearing here: AssetManager builds its asset lookup dictionary once, via
        /// AddExternalAssets, which scans every registered origin's folder a single time - and that
        /// scan runs after every mod's Start() but before any mod's AssetsLoaded(). Register (and,
        /// since generation must finish before the same deadline, generate) during Start(), the same
        /// stage vanilla's own SurvivalCoreSystem/Core.cs register the "survival"/"creative" origins in
        /// (technically StartPre there, but Start is equally before the scan and is where reading files
        /// off disk is actually safe). Doing either of these in AssetsLoaded is too late - confirmed by
        /// the engine logging "Texture asset ... not found" for items whose textures were registered
        /// there even though the files existed on disk by the time AssetsLoaded ran.
        /// </summary>
        public static string GetAssetRootPath(ICoreAPI api)
        {
            return api.GetOrCreateDataPath("ModData/remedyandruin/generatedassets");
        }

        public static string GetGeneratedFilePath(ICoreAPI api, RemedyPoisonCluster cluster, RemedyTextureRole role)
        {
            string category = role == RemedyTextureRole.Liquid ? "liquid" : "potionbase";
            string dir = Path.Combine(GetAssetRootPath(api), "textures", "item", category);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, GetGeneratedFileName(cluster, role));
        }

        /// <summary>
        /// Ensures the generated PNG exists on disk for this cluster, generating it from
        /// baseBmp (tinted, full opacity) if it isn't there yet. Returns the file path either
        /// way. Cheap to call repeatedly - does nothing once the file already exists. Same
        /// cluster color either way - role only changes which source texture gets tinted and
        /// which filename is used. baseBmp must come from a direct-from-disk load
        /// (BitmapExternal(filePath)), not ICoreClientAPI.Assets - the asset system isn't safe
        /// to read from this early (see GetAssetRootPath).
        /// </summary>
        public static string EnsureGeneratedTexture(ICoreAPI api, RemedyPoisonCluster cluster, BitmapRef baseBmp, RemedyTextureRole role)
        {
            string path = GetGeneratedFilePath(api, cluster, role);
            if (!File.Exists(path))
            {
                (byte r, byte g, byte b) = GetClusterTint(cluster);
                using BitmapExternal tinted = LiquidTintTextureSystem.GenerateTintedBitmap(baseBmp, r, g, b, 1f);
                tinted.Save(path);
            }
            return path;
        }
    }
}
