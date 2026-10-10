using System;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B §4 — why a save/load operation did not produce usable data. Every value is
    /// deliberately distinct: the Continue UI must be able to say "no save yet"
    /// (<see cref="Missing"/>) differently from "this save is damaged"
    /// (<see cref="Corrupt"/>) and "this save came from a newer build"
    /// (<see cref="UnsupportedVersion"/>) — and none of them may be turned into a
    /// silent New Game.
    /// </summary>
    public enum SaveIoErrorKind
    {
        /// <summary>Success (or "no error to report").</summary>
        None = 0,

        /// <summary>No save file (and no usable backup) exists.</summary>
        Missing = 1,

        /// <summary>The file exists but is unreadable / not a session envelope / internally inconsistent.</summary>
        Corrupt = 2,

        /// <summary>The file is a session envelope of a format version this build cannot restore.</summary>
        UnsupportedVersion = 3,

        /// <summary>The filesystem itself failed (read/write/replace/delete).</summary>
        Io = 4,

        /// <summary>The caller cancelled, or a session transition cancelled the work.</summary>
        Cancelled = 5,

        /// <summary>The work finished but its session is no longer the live one — the completion was invalidated (§5).</summary>
        Obsolete = 6,

        /// <summary>Another save/load application operation is already in flight (single-flight gate, §4).</summary>
        Busy = 7,

        /// <summary>The request was refused before any I/O (no slot path, null envelope, over the size limit).</summary>
        Rejected = 8,
    }

    /// <summary>
    /// P11B §2 — what the Continue UI needs to know about the single manual save slot.
    ///
    /// Deliberately derived from the SAME validated read that a load performs: there is
    /// no separate sidecar metadata file, so metadata can never drift from the save it
    /// describes. When no usable save exists, <see cref="HasSave"/> is false and
    /// <see cref="ErrorKind"/> says why (never a silent "start new game").
    /// </summary>
    public sealed class SaveSlotMetadata
    {
        /// <summary>True when a readable save exists (in the slot or recovered from the backup).</summary>
        public bool HasSave { get; private set; }

        /// <summary>True when the save was recovered from the backup because the slot was unusable.</summary>
        public bool FromBackup { get; private set; }

        /// <summary><see cref="SaveIoErrorKind.None"/> when <see cref="HasSave"/>; otherwise the reason.</summary>
        public SaveIoErrorKind ErrorKind { get; private set; }

        /// <summary>Human-readable reason; empty on success.</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>Snapshot format version of the save (<see cref="SectSessionSnapshotEnvelope.CurrentVersion"/> when valid).</summary>
        public int FormatVersion { get; private set; }

        /// <summary>When the snapshot was captured (UTC) — informational, for the Continue button.</summary>
        public DateTime SavedAtUtc { get; private set; }

        /// <summary>Roster size at save time.</summary>
        public int DiscipleCount { get; private set; }

        /// <summary>Placed buildings at save time.</summary>
        public int BuildingCount { get; private set; }

        /// <summary>True when a decision-requiring event was outstanding when the snapshot was captured.</summary>
        public bool HasPendingDecision { get; private set; }

        /// <summary>Size of the save file on disk (bytes).</summary>
        public long FileSizeBytes { get; private set; }

        /// <summary>Where the slot lives (informational/diagnostics).</summary>
        public string SlotPath { get; private set; } = string.Empty;

        /// <summary>No usable save: <paramref name="kind"/> is Missing/Corrupt/UnsupportedVersion/Io/…</summary>
        public static SaveSlotMetadata Unusable(SaveIoErrorKind kind, string error, string slotPath)
        {
            return new SaveSlotMetadata
            {
                HasSave = false,
                FromBackup = false,
                ErrorKind = kind,
                Error = error ?? string.Empty,
                SlotPath = slotPath ?? string.Empty,
            };
        }

        /// <summary>Project metadata from a validated envelope (never touches live state).</summary>
        public static SaveSlotMetadata Loaded(
            SectSessionSnapshotEnvelope envelope, long fileSizeBytes, bool fromBackup, string slotPath)
        {
            var snapshot = envelope != null ? envelope.Snapshot : null;
            var economy = snapshot != null ? snapshot.Economy : null;

            return new SaveSlotMetadata
            {
                HasSave = true,
                FromBackup = fromBackup,
                ErrorKind = SaveIoErrorKind.None,
                Error = string.Empty,
                FormatVersion = envelope != null ? envelope.Version : 0,
                SavedAtUtc = envelope != null ? envelope.SavedAtUtc : default(DateTime),
                DiscipleCount = economy != null && economy.Disciples != null ? economy.Disciples.Count : 0,
                BuildingCount = economy != null && economy.PlacedBuildings != null ? economy.PlacedBuildings.Count : 0,
                HasPendingDecision = snapshot != null && snapshot.HasPendingDecision,
                FileSizeBytes = fileSizeBytes,
                SlotPath = slotPath ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// P11B — result of a save (write) application operation. Never throws for a
    /// normal failure: the caller inspects <see cref="Success"/> / <see cref="ErrorKind"/>.
    /// </summary>
    public sealed class SaveSlotResult
    {
        public bool Success { get; private set; }
        public SaveIoErrorKind ErrorKind { get; private set; }
        public string Error { get; private set; } = string.Empty;

        /// <summary>
        /// True when a failed write left the on-disk slot byte-for-byte as it was before
        /// the attempt (the previous valid save is still there — P11B §3). Always true
        /// for a failure that never reached the commit step.
        /// </summary>
        public bool PreviousSavePreserved { get; private set; }

        /// <summary>Metadata of the save that was just written (success only).</summary>
        public SaveSlotMetadata Metadata { get; private set; }

        public static SaveSlotResult Ok(SaveSlotMetadata metadata)
        {
            return new SaveSlotResult
            {
                Success = true,
                ErrorKind = SaveIoErrorKind.None,
                Metadata = metadata,
            };
        }

        public static SaveSlotResult Fail(SaveIoErrorKind kind, string error, bool previousSavePreserved)
        {
            return new SaveSlotResult
            {
                Success = false,
                ErrorKind = kind,
                Error = error ?? string.Empty,
                PreviousSavePreserved = previousSavePreserved,
            };
        }
    }

    /// <summary>
    /// P11B — result of a load (read) application operation. A successful read carries a
    /// detached, already-validated envelope; committing it to live state stays a
    /// main-thread decision (<see cref="SaveSessionService"/>).
    /// </summary>
    public sealed class SaveSlotLoadResult
    {
        public bool Success { get; private set; }
        public SaveIoErrorKind ErrorKind { get; private set; }
        public string Error { get; private set; } = string.Empty;

        /// <summary>True when the slot was unusable and the backup supplied the save.</summary>
        public bool RecoveredFromBackup { get; private set; }

        /// <summary>Informational note about a SUCCESSFUL read (e.g. it recovered from the backup).</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>The detached envelope read from disk (success only).</summary>
        public SectSessionSnapshotEnvelope Envelope { get; private set; }

        /// <summary>Metadata of the read save (present whenever the read got that far).</summary>
        public SaveSlotMetadata Metadata { get; private set; }

        public static SaveSlotLoadResult Ok(SectSessionSnapshotEnvelope envelope, SaveSlotMetadata metadata, bool recoveredFromBackup)
        {
            return new SaveSlotLoadResult
            {
                Success = true,
                ErrorKind = SaveIoErrorKind.None,
                Envelope = envelope,
                Metadata = metadata,
                RecoveredFromBackup = recoveredFromBackup,
            };
        }

        public static SaveSlotLoadResult Fail(SaveIoErrorKind kind, string error, SaveSlotMetadata metadata)
        {
            return new SaveSlotLoadResult
            {
                Success = false,
                ErrorKind = kind,
                Error = error ?? string.Empty,
                Metadata = metadata,
            };
        }
    }

    /// <summary>
    /// P11B §2 — the file layout of the one manual save slot (+ its one backup and the
    /// in-flight temp file). Plain C#, so the repository can be constructed anywhere;
    /// the production root is resolved from <c>Application.persistentDataPath</c> on the
    /// Unity main thread (see <see cref="UnderPersistentDataPath"/>).
    /// </summary>
    public sealed class SaveSlotPaths
    {
        /// <summary>Folder under <c>persistentDataPath</c> that holds the slot.</summary>
        public const string SavesFolderName = "Saves";

        /// <summary>The committed save.</summary>
        public const string SlotFileName = "sect_session.slot";

        /// <summary>The previous committed save (written by a successful replace).</summary>
        public const string BackupFileName = "sect_session.slot.bak";

        /// <summary>The in-flight write. Never treated as a save; deleted on any failure.</summary>
        public const string TempFileName = "sect_session.slot.tmp";

        public SaveSlotPaths(string rootDirectory)
        {
            RootDirectory = rootDirectory ?? string.Empty;
            SlotPath = Combine(SlotFileName);
            BackupPath = Combine(BackupFileName);
            TempPath = Combine(TempFileName);
        }

        /// <summary>Directory that holds the slot, backup and temp file.</summary>
        public string RootDirectory { get; }

        public string SlotPath { get; }
        public string BackupPath { get; }
        public string TempPath { get; }

        /// <summary>
        /// P11B §1 — the production slot location (<c>persistentDataPath/Saves</c>).
        /// Reads <c>Application.persistentDataPath</c>, so this is a UNITY MAIN-THREAD
        /// call: the composition root resolves it once in
        /// <c>GameLifetimeScope.Configure</c> (which runs in <c>Awake</c>) and hands the
        /// instance to the container — the repository never touches a Unity API, and no
        /// worker thread ever resolves a path.
        /// </summary>
        public static SaveSlotPaths UnderPersistentDataPath()
        {
            return new SaveSlotPaths(
                System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, SavesFolderName));
        }

        private string Combine(string fileName)
        {
            if (string.IsNullOrEmpty(RootDirectory)) return string.Empty;
            return System.IO.Path.Combine(RootDirectory, fileName);
        }
    }
}
