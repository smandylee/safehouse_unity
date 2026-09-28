using System.Collections.Generic;
using UnityEngine;

namespace Safehouse.UI
{
    /// <summary>Direct-raid map plate and node tokens, loaded from Resources/Raid.</summary>
    public static class RaidArt
    {
        private static readonly string[] Kinds =
        {
            "start", "container", "loose", "corpse", "noise", "boss_lair", "exit",
        };

        private static Texture2D _plate;
        private static Dictionary<string, Texture2D> _nodes;

        public static Texture2D Plate
        {
            get
            {
                if (_plate == null)
                {
                    _plate = Resources.Load<Texture2D>("Raid/raid-map-plate");
                }

                return _plate;
            }
        }

        public static Texture2D Map(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return null;
            }

            return Resources.Load<Texture2D>("Raid/map-" + mapId);
        }

        public static Texture2D Node(string kind)
        {
            if (_nodes == null)
            {
                _nodes = new Dictionary<string, Texture2D>();
                foreach (var id in Kinds)
                {
                    var file = id == "boss_lair" ? "boss" : id;
                    var texture = Resources.Load<Texture2D>("Raid/raid-node-" + file);
                    if (texture != null)
                    {
                        _nodes[id] = texture;
                    }
                }
            }

            Texture2D found;
            return _nodes.TryGetValue(kind, out found) ? found : null;
        }
    }
}
