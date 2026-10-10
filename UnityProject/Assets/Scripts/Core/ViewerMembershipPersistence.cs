using System;
using System.IO;
using MessagePack;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// P5B persistence — gives viewer membership (status, binding) and
    /// LastActiveAtUtc a real disk path so they survive a session. Loads the
    /// membership slice at startup and re-saves it periodically (and on scope
    /// dispose), because activity can change without publishing a message
    /// (a valid no-op request refreshes it).
    ///
    /// Scope is deliberately the MEMBERSHIP SLICE, not the whole
    /// SectEconomyState: economy/roster still come from MockSectData. Export and
    /// import carry the registry and the disciples' ownership together, so the
    /// registry ⇄ ownership invariants hold across a load (see
    /// SectStateProvider.ExportViewerMembership / TryImportViewerMembership —
    /// import is fully validated and fails closed back to the mock start).
    ///
    /// Local file only — nothing here goes over the interprocess wire, and this
    /// system exposes no MCP tool.
    /// </summary>
    public class ViewerMembershipPersistenceSystem
        : VContainer.Unity.IStartable, VContainer.Unity.ITickable, IDisposable
    {
        public const string FileName = "sect_viewer_membership.msgpack";

        /// <summary>Real-time autosave cadence. Small slice → cheap to rewrite; keeps activity from being lost on a crash.</summary>
        public const float AutoSaveIntervalSeconds = 10f;

        private readonly ISectStateProvider _stateProvider;
        private readonly ISessionRestoreAuthority _restoreAuthority;
        private float _sinceLastSave;

        public ViewerMembershipPersistenceSystem(ISectStateProvider stateProvider)
            : this(stateProvider, null)
        {
        }

        /// <summary>
        /// P11A — <paramref name="restoreAuthority"/> (optional) is the single
        /// orchestration owner of full-session restore. When it reports a full session
        /// is already authoritative, this slice's automatic import REFUSES rather than
        /// overwriting the restored session (see <see cref="Load"/>). The export path
        /// (<see cref="Save"/>) is unaffected.
        /// </summary>
        public ViewerMembershipPersistenceSystem(ISectStateProvider stateProvider,
                                                 ISessionRestoreAuthority restoreAuthority)
        {
            _stateProvider = stateProvider;
            _restoreAuthority = restoreAuthority;
            SavePath = DefaultPath();
        }

        /// <summary>Where the slice is written. Injectable for tests; defaults under Application.persistentDataPath.</summary>
        public string SavePath { get; set; }

        /// <summary>Test seam / diagnostics — how many successful saves have happened.</summary>
        public int SaveCount { get; private set; }

        /// <summary>Test seam / diagnostics — reads attempted (successful imports + rejected ones).</summary>
        public int LoadAttemptCount { get; private set; }

        /// <summary>Reason from the last rejected import ("" when the last load succeeded or there was no file).</summary>
        public string LastLoadError { get; private set; } = string.Empty;

        public static string DefaultPath()
        {
            return Path.Combine(Application.persistentDataPath, FileName);
        }

        /// <summary>Entry point — restore the slice before anything else runs.</summary>
        public void Start()
        {
            Load();
        }

        public void Tick()
        {
            TickWithDelta(Time.unscaledDeltaTime);
        }

        /// <summary>Split out so the autosave cadence is testable without driving Time.</summary>
        public void TickWithDelta(float unscaledDeltaSeconds)
        {
            _sinceLastSave += unscaledDeltaSeconds;
            if (_sinceLastSave < AutoSaveIntervalSeconds) return;
            Save();
        }

        /// <summary>
        /// Read the saved slice and import it. A missing file or any validation
        /// failure is a normal no-op: the mock start state stands (never a partial
        /// restore), and the reason is kept on <see cref="LastLoadError"/>.
        /// </summary>
        public bool Load()
        {
            LoadAttemptCount++;
            LastLoadError = string.Empty;

            // P11A — this slice must never become a competing automatic restore after
            // a full-session load. The full-session orchestrator is the single owner;
            // once it has committed, this narrower slice refuses (fail closed) instead
            // of silently overwriting live state. The export/save path is unaffected.
            if (_restoreAuthority != null && _restoreAuthority.HasFullSessionAuthority)
            {
                LastLoadError = "A full session is already authoritative; refusing to overwrite it " +
                                "with the viewer-membership slice.";
                Debug.Log($"[ViewerMembershipPersistenceSystem] Skipping slice load at '{SavePath}': {LastLoadError}");
                return false;
            }

            if (string.IsNullOrEmpty(SavePath) || !File.Exists(SavePath))
                return false;

            try
            {
                var bytes = File.ReadAllBytes(SavePath);
                var save = MessagePackSerializer.Deserialize<SectViewerMembershipSave>(bytes);
                if (!_stateProvider.TryImportViewerMembership(save, out var failReason))
                {
                    LastLoadError = failReason;
                    Debug.LogWarning($"[ViewerMembershipPersistenceSystem] Ignoring save at '{SavePath}': {failReason}");
                    return false;
                }

                _sinceLastSave = 0f;
                Debug.Log($"[ViewerMembershipPersistenceSystem] Restored viewer membership from '{SavePath}'.");
                return true;
            }
            catch (Exception ex)
            {
                // Corrupt/truncated file must never take the game down — fail closed.
                LastLoadError = ex.Message;
                Debug.LogWarning($"[ViewerMembershipPersistenceSystem] Could not read save at '{SavePath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>Write the membership slice. IO failures are logged, never thrown.</summary>
        public bool Save()
        {
            _sinceLastSave = 0f;
            if (string.IsNullOrEmpty(SavePath)) return false;

            try
            {
                var directory = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var save = _stateProvider.ExportViewerMembership();
                File.WriteAllBytes(SavePath, MessagePackSerializer.Serialize(save));
                SaveCount++;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ViewerMembershipPersistenceSystem] Could not write save at '{SavePath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>VContainer disposes the root scope on app quit — flush the latest activity then.</summary>
        public void Dispose()
        {
            Save();
        }
    }
}
