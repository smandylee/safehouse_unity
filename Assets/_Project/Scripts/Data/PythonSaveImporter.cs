using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Safehouse.Data
{
    /// <summary>What an import did, one line per character, for showing to the player.</summary>
    public sealed class ImportReport
    {
        public List<string> Imported { get; } = new List<string>();

        /// <summary>Characters skipped because this game already has one with that id (its state is newer, so it wins).</summary>
        public List<string> AlreadyPresent { get; } = new List<string>();

        /// <summary>Files that could not be brought over, with the reason.</summary>
        public List<string> Problems { get; } = new List<string>();
    }

    /// <summary>
    /// Copies characters from the Python build's saves into this game's own folder. The Python files are only
    /// ever read - the two games keep separate folders precisely so that nothing here can change a campaign the
    /// Python build owns. Each file goes through the same migrations and checks as any load, so an old save
    /// arrives as a current one, and one that will not pass (an item that no longer exists, say) is reported
    /// and left out rather than imported half-broken.
    /// </summary>
    public static class PythonSaveImporter
    {
        /// <summary>
        /// Where the Python build keeps characters: <c>SAFEHOUSE_DATA_DIR</c> if set, otherwise
        /// <c>%LOCALAPPDATA%\Safehouse</c>, then <c>saves</c>.
        /// </summary>
        public static string DefaultSavesFolder
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("SAFEHOUSE_DATA_DIR");
                var root = string.IsNullOrEmpty(overridden)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Safehouse")
                    : overridden;
                return Path.Combine(root, "saves");
            }
        }

        public static ImportReport Import(string pythonSavesFolder, ProfileRepository target)
        {
            var report = new ImportReport();
            if (!Directory.Exists(pythonSavesFolder))
            {
                report.Problems.Add($"No Python saves folder at {pythonSavesFolder}.");
                return report;
            }

            var existing = new HashSet<string>(target.CharacterIds());
            foreach (var file in Directory.GetFiles(pythonSavesFolder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                try
                {
                    if (!target.TryDecode(File.ReadAllBytes(file), id, out var profile, out var problem))
                    {
                        report.Problems.Add($"{Path.GetFileName(file)}: {problem}");
                    }
                    else if (existing.Contains(profile.ProfileId))
                    {
                        report.AlreadyPresent.Add(profile.DisplayName);
                    }
                    else
                    {
                        target.Save(profile);
                        report.Imported.Add(profile.DisplayName);
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                                              || error is StorageException)
                {
                    report.Problems.Add($"{Path.GetFileName(file)}: {error.Message}");
                }
            }

            return report;
        }
    }
}
