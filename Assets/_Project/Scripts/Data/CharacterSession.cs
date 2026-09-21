using System;
using System.Collections.Generic;
using System.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// The character the player has open, and the one way it changes: build the new version, <see cref="Commit"/>
    /// it (which saves it), and only then does it become the current one. A save that fails throws and leaves
    /// <see cref="Profile"/> as it was, so the screen and the file cannot disagree - the same guarantee the
    /// Python InventorySession gives. Also holds the data-folder lock for as long as the game has it open.
    /// </summary>
    public sealed class CharacterSession : IDisposable
    {
        private DataDirectoryLock _lock;

        private CharacterSession(ProfileRepository repository, Profile profile, DataDirectoryLock folderLock)
        {
            Repository = repository;
            Profile = profile;
            _lock = folderLock;
        }

        /// <summary>Null for an <see cref="Unsaved"/> session.</summary>
        public ProfileRepository Repository { get; }

        public Profile Profile { get; private set; }

        /// <summary>False when nothing is being written: the changes exist only until the game closes.</summary>
        public bool Saves => Repository != null;

        /// <summary>
        /// Opens the data folder (locking it against a second copy of the game) and a character: the one with
        /// <paramref name="preferredId"/> if it exists, otherwise the first by name. A folder with no characters
        /// gets <paramref name="firstCharacter"/>, saved. Problems with individual files, and any restore from a
        /// backup, are added to <paramref name="notices"/>; throws <see cref="StorageException"/> if the folder is
        /// in use or the first character cannot be saved.
        /// </summary>
        public static CharacterSession Open(IReadOnlyDictionary<string, ItemDefinition> catalog, string folder,
            Func<Profile> firstCharacter, string preferredId, ICollection<string> notices)
        {
            var folderLock = DataDirectoryLock.Acquire(folder);
            try
            {
                var repository = new ProfileRepository(folder, catalog);
                var (profiles, problems) = repository.List(notices);
                foreach (var problem in problems)
                {
                    notices?.Add(problem);
                }

                var chosen = profiles.FirstOrDefault(profile => profile.ProfileId == preferredId) ?? profiles.FirstOrDefault();
                if (chosen == null)
                {
                    chosen = firstCharacter();
                    repository.Save(chosen);
                }

                return new CharacterSession(repository, chosen, folderLock);
            }
            catch
            {
                folderLock.Dispose();
                throw;
            }
        }

        /// <summary>A session that never touches the disk, for when the real one cannot be opened.</summary>
        public static CharacterSession Unsaved(Profile profile) => new CharacterSession(null, profile, null);

        /// <summary>Saves the candidate and makes it the current profile. Throws, changing nothing, if it cannot be saved.</summary>
        public void Commit(Profile candidate)
        {
            if (candidate.ProfileId != Profile.ProfileId)
            {
                throw new StorageException("A change can only be committed to the character that is open.");
            }

            Repository?.Save(candidate);
            Profile = candidate;
        }

        /// <summary>Opens another character. The current one is untouched if that fails.</summary>
        public void Switch(string profileId, ICollection<string> notices = null)
        {
            if (Repository == null)
            {
                throw new StorageException("Characters cannot be switched while saving is unavailable.");
            }

            Profile = Repository.Load(profileId, notices);
        }

        /// <summary>The characters that can be opened, by name.</summary>
        public IReadOnlyList<Profile> Characters(ICollection<string> notices = null) =>
            Repository == null ? new[] { Profile } : Repository.List(notices).Profiles;

        public void Dispose()
        {
            _lock?.Dispose();
            _lock = null;
        }
    }
}
