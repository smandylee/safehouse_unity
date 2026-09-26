using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Safehouse.UI
{
    /// <summary>
    /// Item icons, loaded from disk the first time an item is drawn. The C# side of the Python
    /// ui/icons.py, with the same folder layout: <c>icon_cache/48/&lt;item_id&gt;.png</c>, each PNG exactly
    /// the item's footprint at 48 px per cell (a 4x1 rifle is 192x48), so it drops into a cell unscaled.
    ///
    /// There are two kinds. The stash icons (<see cref="Get"/>) are the game's inventory images with the item's
    /// short name baked in, right at the size of the grid cell. The art (<see cref="GetArt"/>, in
    /// <c>icon_cache/art/&lt;item_id&gt;.png</c>, made by tools/import_art.py) is a text-free render at the item's
    /// own proportions, for the LOADOUT slot cards, where the same item is shown at a different size and baked-in
    /// text would come out a different size on each card.
    ///
    /// The icons are Escape from Tarkov game art (via tarkov.dev), for private use only. They are NOT
    /// part of this repository or of any build: <c>icon_cache/</c> is git-ignored and sits next to the
    /// project (next to the .exe in a packaged game), where each person puts their own copy - see the
    /// README. An item without an icon, or a missing folder, simply keeps its placeholder monogram.
    /// </summary>
    public sealed class IconLibrary
    {
        /// <summary>The size folder used; icons are drawn at the screen's 48 px cell pitch.</summary>
        public const int Size = 48;

        private static string _defaultFolder;

        private readonly string _folder;
        private readonly string _artFolder;
        private readonly string _traderFolder;
        private readonly Dictionary<string, Texture2D> _loaded = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> _loadedArt = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> _loadedTraders = new Dictionary<string, Texture2D>();

        /// <param name="rootFolder">The icon_cache folder; <see cref="DefaultFolder"/> when omitted.</param>
        public IconLibrary(string rootFolder = null)
        {
            var root = rootFolder ?? DefaultFolder;
            _folder = Path.Combine(root, Size.ToString());
            _artFolder = Path.Combine(root, "art");
            _traderFolder = Path.Combine(root, "traders");
        }

        /// <summary>
        /// Where icons are looked for: <c>icon_cache</c> beside the Assets folder in the Editor, beside the
        /// game's Data folder (so next to the .exe) in a build. Settable so tests can point elsewhere;
        /// null puts it back to that default.
        /// </summary>
        public static string DefaultFolder
        {
            get => _defaultFolder ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "icon_cache"));
            set => _defaultFolder = value;
        }

        /// <summary>
        /// The most an icon may be stretched to fill a box before it is fitted inside instead. An icon
        /// within this of the box's shape (a 2x2 helmet in a slightly taller card) fills the box and the
        /// stretch is not noticeable; one far from it (a 4x3 rig in that card) would be visibly squashed.
        /// </summary>
        public const float MaxStretch = 1.25f;

        /// <summary>
        /// How to draw an icon in a box so that nothing is ever cropped: stretched to fill when the two
        /// shapes are close, otherwise scaled to fit whole, with the box's own fill showing at the sides.
        /// </summary>
        public static ScaleMode FitInto(float iconAspect, float boxAspect)
        {
            if (iconAspect <= 0 || boxAspect <= 0)
            {
                return ScaleMode.ScaleToFit;
            }

            var mismatch = Mathf.Max(iconAspect, boxAspect) / Mathf.Min(iconAspect, boxAspect);
            return mismatch <= MaxStretch
                ? ScaleMode.StretchToFill
                : ScaleMode.ScaleToFit;
        }

        /// <summary>Whether the icon folder exists at all - false for anyone who has not downloaded icons.</summary>
        public bool HasIcons => Directory.Exists(_folder);

        /// <summary>The item's icon, or null when there is none (no folder, no file, or an unreadable image).</summary>
        public Texture2D Get(string itemId) => Cached(_loaded, _folder, itemId);

        /// <summary>The item's text-free art at its own proportions, or null when there is none.</summary>
        public Texture2D GetArt(string itemId) => Cached(_loadedArt, _artFolder, itemId);

        /// <summary>A trader's portrait (<c>icon_cache/traders/&lt;trader_id&gt;.png</c>, made by tools/import_art.py --traders), or null.</summary>
        public Texture2D GetTrader(string traderId) => Cached(_loadedTraders, _traderFolder, traderId);

        private static Texture2D Cached(Dictionary<string, Texture2D> cache, string folder, string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }

            if (cache.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            var texture = Load(Path.Combine(folder, itemId + ".png"));
            cache[itemId] = texture; // a miss is remembered too, so a missing icon is not re-read every redraw
            return texture;
        }

        private static Texture2D Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                if (texture.LoadImage(File.ReadAllBytes(path)))
                {
                    return texture;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(texture); // Destroy is not allowed outside Play mode
                }

                return null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
