using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Safehouse.Core;
using UnityEngine;

namespace Safehouse.Data
{
    /// <summary>
    /// Where characters are kept: <c>saves/&lt;profile_id&gt;.json</c> in a data folder, each with a
    /// <c>.json.bak</c> holding the save before it. The C# side of the Python ProfileRepository, with its
    /// rules: a save never leaves half a file (see <see cref="AtomicFile"/>), a damaged file is restored from
    /// its backup with the bad one kept in <c>recovery/</c>, and a file that is merely unacceptable (an item
    /// that no longer exists, a schema this version does not know) is reported and left untouched - it is never
    /// replaced by an older backup, which would silently lose progress.
    /// </summary>
    public sealed class ProfileRepository
    {
        private static readonly Regex IdPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled);

        private readonly string _savesFolder;
        private readonly string _recoveryFolder;
        private readonly IReadOnlyDictionary<string, ItemDefinition> _catalog;

        public ProfileRepository(string dataFolder, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            _savesFolder = Path.Combine(dataFolder, "saves");
            _recoveryFolder = Path.Combine(dataFolder, "recovery");
            _catalog = catalog;
            DataFolder = dataFolder;
        }

        public string DataFolder { get; }

        /// <summary>
        /// The Unity game's own folder, apart from the Python build's: <c>SAFEHOUSE_UNITY_DATA_DIR</c> if set,
        /// otherwise a Safehouse folder in Unity's per-user data location.
        /// </summary>
        public static string DefaultFolder
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("SAFEHOUSE_UNITY_DATA_DIR");
                return string.IsNullOrEmpty(overridden)
                    ? Path.Combine(Application.persistentDataPath, "Safehouse")
                    : overridden;
            }
        }

        /// <summary>The file for a character. The id is checked first, which also rules out a path like "..\x".</summary>
        public string PathFor(string profileId)
        {
            if (profileId == null || !IdPattern.IsMatch(profileId))
            {
                throw new StorageException($"Invalid character id: '{profileId}'.");
            }

            return Path.Combine(_savesFolder, profileId + ".json");
        }

        /// <summary>The ids of every character with a file (or only a backup, which <see cref="Load"/> restores).</summary>
        public IReadOnlyList<string> CharacterIds()
        {
            if (!Directory.Exists(_savesFolder))
            {
                return new string[0];
            }

            return Directory.GetFiles(_savesFolder, "*.json*")
                .Select(Path.GetFileName)
                .Where(name => name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".json.bak", StringComparison.Ordinal))
                .Select(name => name.Substring(0, Math.Min(32, name.Length)))
                .Where(id => IdPattern.IsMatch(id))
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        public bool Exists(string profileId) => CharacterIds().Contains(profileId);

        /// <summary>
        /// Every character that can be opened, by name, and a line for each that cannot. One damaged file does
        /// not hide the others.
        /// </summary>
        public (IReadOnlyList<Profile> Profiles, IReadOnlyList<string> Problems) List(ICollection<string> notices = null)
        {
            var profiles = new List<Profile>();
            var problems = new List<string>();
            foreach (var id in CharacterIds())
            {
                try
                {
                    profiles.Add(Load(id, notices));
                }
                catch (StorageException error)
                {
                    problems.Add(error.Message);
                }
            }

            return (profiles.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ProfileId, StringComparer.Ordinal).ToList(), problems);
        }

        // ---- reading ----

        /// <summary>A file that is not a readable save at all (bad UTF-8 or JSON) - as opposed to one that is a
        /// well-formed save this version will not accept.</summary>
        private sealed class DamagedFileException : Exception { }

        public Profile Load(string profileId, ICollection<string> notices = null)
        {
            var path = PathFor(profileId);
            byte[] raw = null;
            try
            {
                if (File.Exists(path))
                {
                    raw = File.ReadAllBytes(path);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException($"Cannot read {Path.GetFileName(path)}. The file was left unchanged.", error);
            }

            if (raw == null)
            {
                return Recover(profileId, path, hasPrimary: false, notices);
            }

            try
            {
                return Decode(raw, profileId);
            }
            catch (DamagedFileException)
            {
                return Recover(profileId, path, hasPrimary: true, notices);
            }
            catch (ValidationException error)
            {
                // Deliberately not restored from the backup: that would quietly throw away everything since.
                throw new StorageException(
                    $"Cannot load {Path.GetFileName(path)}:\n{error.Message}\nThe file was left unchanged.", error);
            }
        }

        /// <summary>Reads one save document as a profile, or says why it cannot be. Nothing is written.</summary>
        public bool TryDecode(byte[] raw, string expectedId, out Profile profile, out string problem)
        {
            try
            {
                profile = Decode(raw, expectedId);
                problem = null;
                return true;
            }
            catch (DamagedFileException)
            {
                problem = "The file is not a readable save (damaged JSON or text).";
            }
            catch (ValidationException error)
            {
                problem = error.Message;
            }

            profile = null;
            return false;
        }

        private Profile Decode(byte[] raw, string expectedId)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw);
            }
            catch (DecoderFallbackException)
            {
                throw new DamagedFileException();
            }

            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1); // a byte-order mark, which the Python side also tolerates
            }

            Newtonsoft.Json.Linq.JToken document;
            try
            {
                document = ProfileSerializer.Parse(text);
            }
            catch (JsonException)
            {
                throw new DamagedFileException();
            }

            var profile = ProfileSerializer.FromJson(ProfileMigrations.Upgrade(document));
            if (expectedId != null && profile.ProfileId != expectedId)
            {
                throw new ValidationException("The character id inside the file does not match its file name.");
            }

            ProfileRules.ValidateAgainstCatalog(profile, _catalog);
            return profile;
        }

        private Profile Recover(string profileId, string path, bool hasPrimary, ICollection<string> notices)
        {
            var name = Path.GetFileName(path);
            var backupPath = path + ".bak";
            if (!File.Exists(backupPath))
            {
                throw new StorageException(hasPrimary
                    ? $"{name} is damaged and has no backup to restore. It was left unchanged; no character was reset or emptied."
                    : $"{name} is missing and has no backup.");
            }

            byte[] backup;
            Profile profile;
            try
            {
                backup = File.ReadAllBytes(backupPath);
                profile = Decode(backup, profileId);
            }
            catch (Exception error) when (error is DamagedFileException || error is ValidationException
                                          || error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException(
                    $"{name} cannot be opened and its backup cannot be used either. Nothing was changed; no character was reset or emptied.",
                    error);
            }

            try
            {
                if (hasPrimary)
                {
                    // Keep the bad file: someone may want to look inside it.
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss");
                    AtomicFile.Write(
                        Path.Combine(_recoveryFolder, $"{profileId}-{stamp}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.json"),
                        File.ReadAllBytes(path));
                }

                AtomicFile.Write(path, backup);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException($"Could not restore {name} from its backup. Nothing was changed.", error);
            }

            notices?.Add($"Recovered {profile.DisplayName} from the backup of its previous save.");
            return profile;
        }

        // ---- writing ----

        /// <summary>
        /// Checks the profile, keeps the current file as the backup, then writes the new one atomically. Throws
        /// <see cref="StorageException"/> and leaves everything as it was if any step fails - including refusing
        /// to overwrite a file it cannot read (a newer version's save, say).
        /// </summary>
        public void Save(Profile profile)
        {
            try
            {
                ProfileRules.ValidateAgainstCatalog(profile, _catalog);
            }
            catch (ValidationException error)
            {
                throw new StorageException(
                    $"{profile.DisplayName} cannot be saved: {error.Message}\nThe requested change was not applied.", error);
            }

            var path = PathFor(profile.ProfileId);
            var backupPath = path + ".bak";
            try
            {
                if (File.Exists(path))
                {
                    var current = File.ReadAllBytes(path);
                    if (!TryDecode(current, profile.ProfileId, out _, out var problem))
                    {
                        // Writing the backup first would replace a good one with this bad file.
                        throw new StorageException(
                            $"The saved file for {profile.DisplayName} cannot be read ({problem}) so it was not overwritten. " +
                            "The requested change was not applied.");
                    }

                    AtomicFile.Write(backupPath, current);
                }
                else if (File.Exists(backupPath))
                {
                    throw new StorageException(
                        $"The saved file for {profile.DisplayName} is missing but its backup exists. " +
                        "Reopen the character to restore the backup first. The requested change was not applied.");
                }

                AtomicFile.Write(path, ProfileSerializer.Serialize(profile));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException(
                    $"Could not save {profile.DisplayName}: {error.Message}\nThe requested change was not applied.", error);
            }
        }

        /// <summary>A new character with a fresh id, saved. At most <see cref="CharacterSheet.MaxCharacters"/> exist.</summary>
        public Profile Create(string displayName, int money = Profile.DefaultMoney,
            int stashWidth = Profile.DefaultStashWidth, int stashHeight = Profile.DefaultStashHeight)
        {
            if (CharacterIds().Count >= CharacterSheet.MaxCharacters)
            {
                throw new StorageException($"At most {CharacterSheet.MaxCharacters} characters can be kept.");
            }

            var profile = Profile.CreateNew(Guid.NewGuid().ToString("N"), displayName, money, stashWidth, stashHeight);
            Save(profile);
            return profile;
        }

        /// <summary>Removes a character, backup first so it cannot come back by being "recovered". Files in recovery/ stay.</summary>
        public void Delete(string profileId)
        {
            var path = PathFor(profileId);
            try
            {
                File.Delete(path + ".bak");
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException($"Could not delete {Path.GetFileName(path)}: {error.Message}", error);
            }
        }
    }
}
