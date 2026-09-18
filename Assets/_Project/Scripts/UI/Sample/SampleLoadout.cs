using System.Collections.Generic;
using Safehouse.Core;

namespace Safehouse.UI.Sample
{
    /// <summary>
    /// The rig and backpack shown in the GEAR screen's CARRIED panel, filled with real catalog items
    /// like <see cref="SampleStash"/>. Stand-in data until a character's real loadout can be loaded
    /// from a save; instance ids sit in their own ranges so they never collide with the stash's.
    /// </summary>
    public static class SampleLoadout
    {
        public const int RigWidth = 6;
        public const int RigHeight = 4;
        public const int BackpackWidth = 6;
        public const int BackpackHeight = 8;

        private static readonly string[] RigItemIds =
        {
            "ak-12-545x39-30-round-magazine",
            "ak-12-545x39-30-round-magazine",
            "ak-74-545x39-6l31-60-round-magazine",
            "cat-hemostatic-tourniquet",
            "cat-hemostatic-tourniquet",
        };

        private static readonly string[] BackpackItemIds =
        {
            "item-case",
            "magazine-case",
            "documents-case",
            "dogtag-case",
        };

        public static StashGrid BuildRig(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            SampleStash.Fill(catalog, RigItemIds, RigWidth, RigHeight, firstInstanceNumber: 1000);

        public static StashGrid BuildBackpack(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            SampleStash.Fill(catalog, BackpackItemIds, BackpackWidth, BackpackHeight, firstInstanceNumber: 2000);
    }
}
