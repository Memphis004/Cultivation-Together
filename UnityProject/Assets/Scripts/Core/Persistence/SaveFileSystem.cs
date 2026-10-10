using System.IO;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B §3 — the filesystem operations the save slot needs, behind one seam so the
    /// "a failed write must preserve the previous valid save" contract can be tested
    /// with an injected fault instead of an OS-permission trick (directory read-only
    /// attributes do not stop file creation on Windows).
    ///
    /// System.IO only — no Unity API — because <see cref="SaveSlotRepository"/> runs on
    /// a worker thread (§6/Common-Rules 4).
    /// </summary>
    public interface ISaveFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);

        /// <summary>Throws when the file is missing (FileNotFoundException) or unreadable.</summary>
        long GetFileLength(string path);

        /// <summary>Throws when the file is missing (FileNotFoundException) or unreadable.</summary>
        byte[] ReadAllBytes(string path);

        /// <summary>
        /// Write the whole content and flush it to disk. The file must end up complete or
        /// unchanged — a partially written temp file must never be mistaken for a save.
        /// </summary>
        void WriteAllBytes(string path, byte[] bytes);

        /// <summary>
        /// Replace <paramref name="destination"/> with <paramref name="source"/>, keeping the
        /// old destination as <paramref name="backup"/>. Implementations may throw
        /// <see cref="System.PlatformNotSupportedException"/>/<see cref="System.NotSupportedException"/>
        /// on platforms without a native replace; the repository then falls back to a
        /// move-aside strategy.
        /// </summary>
        void ReplaceWithBackup(string source, string destination, string backup);

        /// <summary>Move a file. Throws when the destination already exists.</summary>
        void Move(string source, string destination);

        /// <summary>Delete a file; missing files are not an error.</summary>
        void Delete(string path);
    }

    /// <summary>P11B — the real filesystem behind <see cref="ISaveFileSystem"/>.</summary>
    public sealed class SystemSaveFileSystem : ISaveFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public long GetFileLength(string path) => new FileInfo(path).Length;

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void WriteAllBytes(string path, byte[] bytes)
        {
            // Flush(true) pushes the bytes to the device before we return, so the temp file
            // is durable before it is swapped in (a power loss can then lose the new save,
            // but never turn the current one into a truncated file).
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        public void ReplaceWithBackup(string source, string destination, string backup)
        {
            File.Replace(source, destination, backup);
        }

        public void Move(string source, string destination) => File.Move(source, destination);

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
