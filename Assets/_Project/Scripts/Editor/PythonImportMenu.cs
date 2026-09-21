using System.Text;
using Safehouse.Data;
using UnityEditor;

namespace Safehouse.Editor
{
    /// <summary>
    /// Safehouse > Import Python Characters: copies the Python build's characters into this game's own data
    /// folder (see <see cref="PythonSaveImporter"/>). The Python saves are only read. Characters already here are
    /// left as they are, so it is safe to run again after playing.
    /// </summary>
    public static class PythonImportMenu
    {
        private const string Title = "Import Python characters";

        [MenuItem("Safehouse/Import Python Characters")]
        public static void Run()
        {
            var target = ProfileRepository.DefaultFolder;
            var source = PythonSaveImporter.DefaultSavesFolder;

            DataDirectoryLock folderLock;
            try
            {
                folderLock = DataDirectoryLock.Acquire(target);
            }
            catch (StorageException)
            {
                EditorUtility.DisplayDialog(Title,
                    "The game is using its save folder right now (is Play mode running?).\nExit Play mode, then try again.",
                    "OK");
                return;
            }

            using (folderLock)
            {
                var report = PythonSaveImporter.Import(source, new ProfileRepository(target, CatalogLoader.Load()));

                var text = new StringBuilder();
                text.AppendLine($"From: {source}");
                text.AppendLine($"To:   {target}");
                text.AppendLine();
                text.AppendLine($"Imported ({report.Imported.Count}): {string.Join(", ", report.Imported)}");
                text.AppendLine($"Already here, left as they are ({report.AlreadyPresent.Count}): {string.Join(", ", report.AlreadyPresent)}");
                if (report.Problems.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine($"Could not import ({report.Problems.Count}):");
                    foreach (var problem in report.Problems)
                    {
                        text.AppendLine("  " + problem);
                    }
                }

                EditorUtility.DisplayDialog(Title, text.ToString(), "OK");
            }
        }
    }
}
