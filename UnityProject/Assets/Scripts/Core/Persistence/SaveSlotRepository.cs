using System;
using System.Threading;
using MessagePack;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B §1/§3 — the one manual save slot on disk, built on the P11A envelope
    /// (<see cref="SectSessionSnapshotEnvelope"/>).
    ///
    /// Plain C# + System.IO + MessagePack ONLY: no Unity API, so this whole type runs on
    /// a worker thread and may only ever touch DETACHED data (the envelope the caller
    /// hands in). It never enumerates or mutates live gameplay state
    /// (Common-Rules 4).
    ///
    /// Layout (§2 — one slot, one backup, no browser):
    ///   Saves/sect_session.slot      the committed save
    ///   Saves/sect_session.slot.bak  the previous committed save
    ///   Saves/sect_session.slot.tmp  the in-flight write (never a readable save)
    ///
    /// Write: serialize → size limit → pre-commit checks → write temp → verify temp →
    /// platform-safe replace (the previous slot becomes the backup). ANY failure deletes
    /// the temp and leaves the slot + backup untouched, so a failed save can never lose
    /// the previous one. The replace is the commit point and is not cancellable: a
    /// caller that cancels after it gets a save, not a half-written slot.
    ///
    /// Read: the slot first; when the slot is missing/corrupt, recover from the backup.
    /// A NEWER-format slot is reported as UnsupportedVersion and is deliberately NOT
    /// downgraded to the backup — that is a migration decision, not a fallback
    /// (§4: distinguish missing / corrupt / unsupported-version / I/O).
    /// </summary>
    public sealed class SaveSlotRepository
    {
        /// <summary>
        /// Default ceiling for the save file AND for the deserializer's input. Far above
        /// any MVP session; enforced before and after the read so a bogus/hostile length
        /// cannot turn into an unbounded allocation (§3).
        /// </summary>
        public const int DefaultMaxSaveFileBytes = 8 * 1024 * 1024;

        /// <summary>Object-graph depth ceiling handed to the MessagePack security model.</summary>
        public const int MaxObjectGraphDepth = 64;

        /// <summary>
        /// Serializer options for the slot: Standard resolver (every shared type carries
        /// <c>[MessagePackObject]</c>/<c>[Key]</c>) plus untrusted-input security (depth
        /// limit), because a save file is data we did not produce in this process.
        /// </summary>
        public static readonly MessagePackSerializerOptions SerializerOptions =
            MessagePackSerializerOptions.Standard.WithSecurity(
                MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(MaxObjectGraphDepth));

        private readonly ISaveFileSystem _fileSystem;
        private readonly int _maxSaveFileBytes;

        public SaveSlotRepository(ISaveFileSystem fileSystem, SaveSlotPaths paths)
            : this(fileSystem, paths, DefaultMaxSaveFileBytes)
        {
        }

        public SaveSlotRepository(ISaveFileSystem fileSystem, SaveSlotPaths paths, int maxSaveFileBytes)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _maxSaveFileBytes = maxSaveFileBytes > 0 ? maxSaveFileBytes : DefaultMaxSaveFileBytes;
        }

        /// <summary>Where the slot/backup/temp live.</summary>
        public SaveSlotPaths Paths { get; }

        /// <summary>How many bytes a save may occupy.</summary>
        public int MaxSaveFileBytes => _maxSaveFileBytes;

        // ---------- write ----------

        /// <summary>
        /// Write <paramref name="envelope"/> as the slot without a commit guard (tests /
        /// direct callers). Production goes through <see cref="SaveSessionService"/>,
        /// which supplies the session guard.
        /// </summary>
        public SaveSlotResult Write(SectSessionSnapshotEnvelope envelope, CancellationToken cancellationToken)
        {
            return Write(envelope, cancellationToken, null);
        }

        /// <summary>
        /// Write <paramref name="envelope"/> as the slot.
        ///
        /// <paramref name="commitGuard"/> is asked twice — before the temp file is written
        /// and again immediately before the irreversible replace — and a false answer
        /// aborts with <see cref="SaveIoErrorKind.Obsolete"/> leaving the slot untouched.
        /// That is how a save from a session the player has already left can never
        /// overwrite the new session's slot (§5). It may be called on a worker thread, so
        /// it must only read thread-safe state (no Unity API).
        /// </summary>
        public SaveSlotResult Write(
            SectSessionSnapshotEnvelope envelope,
            CancellationToken cancellationToken,
            Func<bool> commitGuard)
        {
            if (string.IsNullOrEmpty(Paths.SlotPath))
                return SaveSlotResult.Fail(SaveIoErrorKind.Rejected,
                    "No save slot path is configured.", true);

            if (envelope == null || envelope.Snapshot == null)
                return SaveSlotResult.Fail(SaveIoErrorKind.Rejected,
                    "Refusing to save: the session envelope (or its payload) is null.", true);

            if (cancellationToken.IsCancellationRequested) return Cancelled();

            // 1. serialize detached data (never live state, never on the main thread).
            byte[] bytes;
            try
            {
                bytes = MessagePackSerializer.Serialize(envelope, SerializerOptions, cancellationToken);
            }
            catch (Exception ex) when (IsCancellation(ex))
            {
                // MessagePack WRAPS a cancellation raised while writing into its own
                // exception type, so the inner chain has to be inspected — otherwise a
                // cancelled save would be reported as an I/O failure.
                return Cancelled();
            }
            catch (Exception ex)
            {
                return SaveSlotResult.Fail(SaveIoErrorKind.Io,
                    "Could not serialize the session: " + ex.Message, true);
            }

            if (bytes == null || bytes.Length == 0)
                return SaveSlotResult.Fail(SaveIoErrorKind.Rejected,
                    "Refusing to save: the serialized session is empty.", true);

            if (bytes.Length > _maxSaveFileBytes)
                return SaveSlotResult.Fail(SaveIoErrorKind.Rejected,
                    $"Refusing to save: the session is {bytes.Length} bytes, over the {_maxSaveFileBytes} byte save limit.", true);

            // 2. pre-commit checks — cancellation and session validity, before any file exists.
            if (cancellationToken.IsCancellationRequested) return Cancelled();
            if (commitGuard != null && !commitGuard()) return Obsolete();

            // 3. write the temp file. The current slot is untouched up to here.
            try
            {
                if (!_fileSystem.DirectoryExists(Paths.RootDirectory))
                    _fileSystem.CreateDirectory(Paths.RootDirectory);

                _fileSystem.WriteAllBytes(Paths.TempPath, bytes);

                if (cancellationToken.IsCancellationRequested)
                {
                    DeleteTemp();
                    return Cancelled();
                }

                if (commitGuard != null && !commitGuard())
                {
                    DeleteTemp();
                    return Obsolete();
                }

                // 4. verify BEFORE the swap: the previous valid save is only ever replaced
                //    by a save we have already read back successfully.
                string verifyReason;
                if (!VerifyTemp(out verifyReason))
                {
                    DeleteTemp();
                    return SaveSlotResult.Fail(SaveIoErrorKind.Io, verifyReason, true);
                }
            }
            catch (Exception ex)
            {
                DeleteTemp();
                return SaveSlotResult.Fail(SaveIoErrorKind.Io,
                    $"Could not write the save at '{Paths.SlotPath}': {ex.Message}", true);
            }

            // 5. commit. Not cancellable: once the swap starts the old save is either the
            //    backup or still the slot, never a mix.
            try
            {
                SwapInTemp();
            }
            catch (Exception ex)
            {
                DeleteTemp();
                bool slotIntact = _fileSystem.FileExists(Paths.SlotPath);
                return SaveSlotResult.Fail(SaveIoErrorKind.Io,
                    $"Could not replace the save at '{Paths.SlotPath}': {ex.Message}", slotIntact);
            }

            return SaveSlotResult.Ok(SaveSlotMetadata.Loaded(envelope, bytes.Length, false, Paths.SlotPath));
        }

        /// <summary>
        /// Replace the slot with the verified temp file, keeping the previous save as the
        /// backup. Prefers a native atomic replace; falls back to move-aside on platforms
        /// that do not support it (with a best-effort rollback so a failed swap cannot
        /// lose the previous save).
        /// </summary>
        private void SwapInTemp()
        {
            if (!_fileSystem.FileExists(Paths.SlotPath))
            {
                // First save on this machine: nothing to back up.
                _fileSystem.Move(Paths.TempPath, Paths.SlotPath);
                return;
            }

            try
            {
                _fileSystem.ReplaceWithBackup(Paths.TempPath, Paths.SlotPath, Paths.BackupPath);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (NotSupportedException)
            {
            }

            // Fallback: previous slot → backup, then temp → slot.
            if (_fileSystem.FileExists(Paths.BackupPath)) _fileSystem.Delete(Paths.BackupPath);
            _fileSystem.Move(Paths.SlotPath, Paths.BackupPath);
            try
            {
                _fileSystem.Move(Paths.TempPath, Paths.SlotPath);
            }
            catch
            {
                TryRestoreSlotFromBackup();
                throw;
            }
        }

        private void TryRestoreSlotFromBackup()
        {
            try
            {
                if (!_fileSystem.FileExists(Paths.SlotPath) && _fileSystem.FileExists(Paths.BackupPath))
                    _fileSystem.Move(Paths.BackupPath, Paths.SlotPath);
            }
            catch (Exception)
            {
                // Best effort only: the caller reports the failed write either way.
            }
        }

        /// <summary>
        /// Read the temp file back and deserialize it before it is allowed to replace the
        /// slot. Cheap (single save) insurance that a truncated/garbled write cannot
        /// destroy the previous valid save.
        /// </summary>
        private bool VerifyTemp(out string reason)
        {
            reason = string.Empty;
            try
            {
                long length = _fileSystem.GetFileLength(Paths.TempPath);
                if (length <= 0)
                {
                    reason = "Write verification failed: the temp save is empty.";
                    return false;
                }
                if (length > _maxSaveFileBytes)
                {
                    reason = $"Write verification failed: the temp save is {length} bytes, over the {_maxSaveFileBytes} byte limit.";
                    return false;
                }

                var bytes = _fileSystem.ReadAllBytes(Paths.TempPath);
                if (bytes == null || bytes.Length == 0)
                {
                    reason = "Write verification failed: the temp save is empty.";
                    return false;
                }

                var envelope = MessagePackSerializer.Deserialize<SectSessionSnapshotEnvelope>(bytes, SerializerOptions);
                if (envelope == null || envelope.Snapshot == null || envelope.Version <= 0)
                {
                    reason = "Write verification failed: the temp save is not a readable session envelope.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = "Write verification failed: " + ex.Message;
                return false;
            }

            return true;
        }

        // ---------- read ----------

        /// <summary>
        /// Read the slot, recovering from the backup when the slot is missing or corrupt.
        /// Never throws for bad data — the caller gets an explicit
        /// <see cref="SaveIoErrorKind"/> instead (and therefore can never mistake a failed
        /// Continue for a fresh game).
        /// </summary>
        public SaveSlotLoadResult Read(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Paths.SlotPath))
                return SaveSlotLoadResult.Fail(SaveIoErrorKind.Rejected,
                    "No save slot path is configured.", SaveSlotMetadata.Unusable(SaveIoErrorKind.Rejected, "No save slot path is configured.", string.Empty));

            if (cancellationToken.IsCancellationRequested)
                return CancelledLoad();

            bool slotExists = _fileSystem.FileExists(Paths.SlotPath);
            SaveSlotLoadResult primary = slotExists
                ? ReadOne(Paths.SlotPath, false, cancellationToken)
                : null;

            if (primary != null && primary.Success) return primary;

            // Two failures are reported instead of falling back to the backup:
            //   * UnsupportedVersion — silently loading an older save over a newer-format
            //     one would be data loss, not recovery (it needs a migration decision).
            //   * Io — the filesystem itself is unhappy; reporting that beats quietly
            //     restoring stale data whose own read may also fail.
            // Cancelled is propagated as-is.
            if (primary != null &&
                (primary.ErrorKind == SaveIoErrorKind.UnsupportedVersion ||
                 primary.ErrorKind == SaveIoErrorKind.Cancelled ||
                 primary.ErrorKind == SaveIoErrorKind.Io))
            {
                return primary;
            }

            if (cancellationToken.IsCancellationRequested) return CancelledLoad();

            if (_fileSystem.FileExists(Paths.BackupPath))
            {
                var backup = ReadOne(Paths.BackupPath, true, cancellationToken);
                if (backup.Success)
                {
                    backup.Note = primary != null
                        ? $"Primary save was unusable ({primary.Error}); recovered from the backup."
                        : "Primary save was missing; recovered from the backup.";
                    return backup;
                }

                if (backup.ErrorKind == SaveIoErrorKind.Cancelled) return backup;

                // Both unusable: report the slot's problem (it is the authoritative copy).
                return primary ?? backup;
            }

            if (primary == null)
            {
                return SaveSlotLoadResult.Fail(SaveIoErrorKind.Missing,
                    $"No save exists at '{Paths.SlotPath}' (and there is no backup).",
                    SaveSlotMetadata.Unusable(SaveIoErrorKind.Missing,
                        $"No save exists at '{Paths.SlotPath}' (and there is no backup).", Paths.SlotPath));
            }

            return primary;
        }

        /// <summary>
        /// P11B §2 — metadata for the Continue UI, from the same validated read a load
        /// would do (no sidecar file, so it cannot drift from the save).
        /// </summary>
        public SaveSlotMetadata ReadMetadata(CancellationToken cancellationToken)
        {
            var result = Read(cancellationToken);
            if (result.Metadata != null) return result.Metadata;
            return SaveSlotMetadata.Unusable(result.ErrorKind, result.Error, Paths.SlotPath);
        }

        private SaveSlotLoadResult ReadOne(string path, bool fromBackup, CancellationToken cancellationToken)
        {
            long length;
            try
            {
                length = _fileSystem.GetFileLength(path);
            }
            catch (System.IO.FileNotFoundException)
            {
                return MissingOne(path);
            }
            catch (Exception ex)
            {
                return IoOne(path, ex);
            }

            if (length <= 0)
                return CorruptOne(path, "the file is empty");

            if (length > _maxSaveFileBytes)
                return CorruptOne(path,
                    $"the file is {length} bytes, over the {_maxSaveFileBytes} byte save limit");

            byte[] bytes;
            try
            {
                bytes = _fileSystem.ReadAllBytes(path);
            }
            catch (System.IO.FileNotFoundException)
            {
                return MissingOne(path);
            }
            catch (Exception ex)
            {
                return IoOne(path, ex);
            }

            if (bytes == null || bytes.Length == 0)
                return CorruptOne(path, "the file is empty");

            if (bytes.Length > _maxSaveFileBytes)
                return CorruptOne(path,
                    $"the file is {bytes.Length} bytes, over the {_maxSaveFileBytes} byte save limit");

            SectSessionSnapshotEnvelope envelope;
            try
            {
                envelope = MessagePackSerializer.Deserialize<SectSessionSnapshotEnvelope>(
                    bytes, SerializerOptions, cancellationToken);
            }
            catch (Exception ex) when (IsCancellation(ex))
            {
                // Same wrapping caveat as the serialize path: a cancelled token is a
                // cancellation, not a corrupt file.
                return CancelledLoad();
            }
            catch (Exception ex)
            {
                return CorruptOne(path, "it could not be deserialized (" + ex.Message + ")");
            }

            if (envelope == null)
                return CorruptOne(path, "it did not contain a session envelope");

            if (envelope.Version == 0)
                return UnsupportedVersionOne(path,
                    "it has no readable format version (missing or pre-versioned)");

            if (envelope.Version > SectSessionSnapshotEnvelope.CurrentVersion)
                return UnsupportedVersionOne(path,
                    $"it was written by a newer build (format {envelope.Version} > {SectSessionSnapshotEnvelope.CurrentVersion})");

            if (envelope.Version < SectSessionSnapshotEnvelope.CurrentVersion)
                return UnsupportedVersionOne(path,
                    $"its format {envelope.Version} is older than {SectSessionSnapshotEnvelope.CurrentVersion} and no migration is defined");

            if (envelope.Snapshot == null)
                return CorruptOne(path, "the envelope has no session payload");

            return SaveSlotLoadResult.Ok(
                envelope, SaveSlotMetadata.Loaded(envelope, length, fromBackup, Paths.SlotPath), fromBackup);
        }

        // ---------- failure shapes ----------

        /// <summary>
        /// True when <paramref name="ex"/> — or anything it wraps — is a cancellation.
        /// MessagePack reports a cancellation raised while writing a graph as
        /// <c>MessagePackSerializationException</c> with the OCE as its inner exception.
        /// </summary>
        private static bool IsCancellation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is OperationCanceledException) return true;
            }
            return false;
        }

        private SaveSlotResult Cancelled()
        {
            return SaveSlotResult.Fail(SaveIoErrorKind.Cancelled,
                "The save was cancelled before it was committed; the previous save is untouched.", true);
        }

        private SaveSlotResult Obsolete()
        {
            return SaveSlotResult.Fail(SaveIoErrorKind.Obsolete,
                "The save belongs to a session that is no longer current; it was not written.", true);
        }

        private SaveSlotLoadResult CancelledLoad()
        {
            return SaveSlotLoadResult.Fail(SaveIoErrorKind.Cancelled,
                "The load was cancelled.", SaveSlotMetadata.Unusable(SaveIoErrorKind.Cancelled, "The load was cancelled.", Paths.SlotPath));
        }

        private SaveSlotLoadResult MissingOne(string path)
        {
            string reason = $"No save file at '{path}'.";
            return SaveSlotLoadResult.Fail(SaveIoErrorKind.Missing,
                reason, SaveSlotMetadata.Unusable(SaveIoErrorKind.Missing, reason, Paths.SlotPath));
        }

        private SaveSlotLoadResult CorruptOne(string path, string why)
        {
            string reason = $"The save at '{path}' is corrupt: {why}.";
            return SaveSlotLoadResult.Fail(SaveIoErrorKind.Corrupt,
                reason, SaveSlotMetadata.Unusable(SaveIoErrorKind.Corrupt, reason, Paths.SlotPath));
        }

        private SaveSlotLoadResult UnsupportedVersionOne(string path, string why)
        {
            string reason = $"The save at '{path}' has an unsupported format: {why}.";
            return SaveSlotLoadResult.Fail(SaveIoErrorKind.UnsupportedVersion,
                reason, SaveSlotMetadata.Unusable(SaveIoErrorKind.UnsupportedVersion, reason, Paths.SlotPath));
        }

        private SaveSlotLoadResult IoOne(string path, Exception ex)
        {
            string reason = $"Could not read the save at '{path}': {ex.Message}.";
            return SaveSlotLoadResult.Fail(SaveIoErrorKind.Io,
                reason, SaveSlotMetadata.Unusable(SaveIoErrorKind.Io, reason, Paths.SlotPath));
        }

        private void DeleteTemp()
        {
            try
            {
                _fileSystem.Delete(Paths.TempPath);
            }
            catch (Exception)
            {
                // Best effort: an orphan temp file is inert (it is never read as a save).
            }
        }
    }
}
