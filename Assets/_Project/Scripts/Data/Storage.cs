using System;
using System.IO;

namespace Safehouse.Data
{
    /// <summary>
    /// Something went wrong reading or writing a save. The message says what happened to the file (the rule is
    /// that a failure leaves the player's data as it was) and is written to be shown to the player as-is.
    /// </summary>
    public sealed class StorageException : Exception
    {
        public StorageException(string message) : base(message) { }
        public StorageException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Writes a file so that the target is always either the complete old contents or the complete new ones,
    /// never half of each: write a sibling temp file, flush it to disk, then swap it in. The C# side of the
    /// Python storage.atomic_write_bytes.
    /// </summary>
    public static class AtomicFile
    {
        public static void Write(string path, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory,
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true); // to the disk, not just the OS cache: a crash right after must not lose it
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // Nothing more can be done about a temp file that will not go; the original error matters.
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Keeps two copies of the game from writing the same data folder at once, which would interleave their
    /// saves. Held for as long as the game runs; the OS releases it if the game crashes.
    /// </summary>
    public sealed class DataDirectoryLock : IDisposable
    {
        private FileStream _stream;

        private DataDirectoryLock(FileStream stream)
        {
            _stream = stream;
        }

        public static DataDirectoryLock Acquire(string directory)
        {
            Directory.CreateDirectory(directory);
            try
            {
                var stream = new FileStream(Path.Combine(directory, ".safehouse.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new DataDirectoryLock(stream);
            }
            catch (IOException error)
            {
                throw new StorageException(
                    "This data folder is already in use by another copy of Safehouse. Close it and try again.", error);
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
