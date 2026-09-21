using System.Collections.Generic;
using Safehouse.Core;

namespace Safehouse.UI.Sample
{
    /// <summary>
    /// The character a brand-new data folder starts with, so the GEAR screen has something to show before any
    /// character has been imported or created: a real profile whose stash, rig, backpack and worn gear are the
    /// sample items (<see cref="SampleStash"/>, <see cref="SampleLoadout"/>). It is saved like any other, so it
    /// can be played with and changed; it is never re-created once the folder has a character.
    /// </summary>
    public static class SampleCharacter
    {
        public const string ProfileId = "5a3b1e0c5a3b1e0c5a3b1e0c5a3b1e0c";
        public const string Name = "Sample Operator";

        public static Profile Build(IReadOnlyDictionary<string, ItemDefinition> catalog) =>
            Profile.CreateNew(ProfileId, Name).With(
                stash: SampleStash.Build(catalog, Profile.DefaultStashWidth, Profile.DefaultStashHeight),
                rig: SampleLoadout.BuildRig(catalog),
                backpack: SampleLoadout.BuildBackpack(catalog),
                loadout: SampleLoadout.BuildEquipped(catalog));
    }
}
