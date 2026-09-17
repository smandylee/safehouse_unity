using System.Collections.Generic;
using UnityEngine;

namespace Safehouse.UI
{
    /// <summary>
    /// Border colour by rarity, matching the Kit artboard. Rarity marks a cell's border only - never
    /// its fill - so an item stays readable at 46px regardless of how rare it is.
    /// </summary>
    public static class RarityPalette
    {
        private static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            ["Common"] = new Color(0.420f, 0.463f, 0.518f),
            ["Uncommon"] = new Color(0.373f, 0.549f, 0.431f),
            ["Rare"] = new Color(0.306f, 0.478f, 0.620f),
            ["Epic"] = new Color(0.541f, 0.420f, 0.659f),
            ["Legendary"] = new Color(0.753f, 0.541f, 0.243f),
        };

        private static readonly Color Fallback = new Color(0.420f, 0.463f, 0.518f);

        public static Color For(string rarity) =>
            rarity != null && Colors.TryGetValue(rarity, out var color) ? color : Fallback;
    }
}
