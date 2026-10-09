using System;
using System.Collections.Generic;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// P3 — Task Assignment panel presenter. Draft/Confirm model (§5):
    /// - baseline = live state snapshot at open/rebuild
    /// - draft = user's pending dropdown selection per disciple row
    /// - Confirm calls TryAssignTask ONLY for changed entries (requesterId
    ///   "SECT_MASTER" — the trusted GM/player identity; P5B Hybrid permissions
    ///   apply the SAME rule to this id: an unowned NPC / player-controlled
    ///   disciple is fair game, while a Viewer disciple is protected until its
    ///   owner has been inactive for strictly more than 10 real-time minutes.
    ///   There is no AI-GM bypass).
    /// - multi-row confirm is NOT atomic: successful rows update the baseline,
    ///   failures keep their draft + per-row error text (no blind rollback).
    /// - DiscipleTaskChangedMessage from another path (AI GM / bridge) does not
    ///   overwrite dirty drafts: affected rows are marked conflicted and must be
    ///   re-selected before they can commit (§6).
    /// Permission is NEVER computed client-side as OwnerType != Npc — the
    /// authority is CheckTaskPermission (read-only) for display and TryAssignTask
    /// for mutation; the panel renders the ownership label (Npc = "ศิษย์สำนัก",
    /// Viewer/Player = owner id) plus P5B owner-protection + remaining time.
    /// </summary>
    public class TaskAssignmentPresenter : UIPresenter<TaskAssignmentView>
    {
        private static readonly string RequesterId = "SECT_MASTER";

        private readonly ISectStateProvider _stateProvider;
        private readonly ISubscriber<DiscipleTaskChangedMessage> _taskChangedSubscriber;
        // P9A — reflect an external Manual/Auto change (ownership switch, manual assign)
        // on the row's Auto toggle. Optional so existing construction sites stay valid.
        private readonly ISubscriber<DiscipleControlModeChangedMessage> _controlModeChangedSubscriber;

        private System.Action _closeCallback;
        private IDisposable _taskChangedSubscription;
        private IDisposable _controlModeChangedSubscription;

        private readonly List<string> _knownTasks = new List<string>();
        private readonly List<TaskAssignmentRowCell> _rows = new List<TaskAssignmentRowCell>();

        /// <summary>discipleId → baseline task at last rebuild.</summary>
        private readonly Dictionary<string, string> _baseline = new Dictionary<string, string>();

        /// <summary>discipleId → draft task id (present only when changed from baseline).</summary>
        private readonly Dictionary<string, string> _drafts = new Dictionary<string, string>();

        /// <summary>discipleId → per-row error from the last confirm attempt ("" clears).</summary>
        private readonly Dictionary<string, string> _rowErrors = new Dictionary<string, string>();

        /// <summary>discipleId rows whose baseline changed externally while dirty.</summary>
        private readonly HashSet<string> _conflicts = new HashSet<string>();

        public TaskAssignmentPresenter(
            ISectStateProvider stateProvider,
            ISubscriber<DiscipleTaskChangedMessage> taskChangedSubscriber,
            ISubscriber<DiscipleControlModeChangedMessage> controlModeChangedSubscriber = null)
        {
            _stateProvider = stateProvider;
            _taskChangedSubscriber = taskChangedSubscriber;
            _controlModeChangedSubscriber = controlModeChangedSubscriber;
        }

        protected override void OnViewBound()
        {
            View.CloseClicked += OnCloseClicked;
            View.ConfirmClicked += OnConfirmClicked;
            View.CancelClicked += OnCancelClicked;

            // §6: external task changes arrive while the panel may be open.
            // The subscription lives only as long as the panel (Dispose runs via
            // UIService.Close) — reopen re-subscribes exactly once.
            if (_taskChangedSubscriber != null)
                _taskChangedSubscription = _taskChangedSubscriber.Subscribe(OnTaskChangedExternally);
            if (_controlModeChangedSubscriber != null)
                _controlModeChangedSubscription = _controlModeChangedSubscriber.Subscribe(OnControlModeChangedExternally);
        }

        public override void OnOpen(object args)
        {
            _closeCallback = (args as TaskAssignmentArgs)?.CloseCallback;

            // Rebuild on every open — baseline snaps to the live state, drafts
            // reset (ResourcePopup re-prime pattern).
            Rebuild();
        }

        public override void Dispose()
        {
            // §8: unsubscribe through UIService's actual lifecycle — UIService.Close
            // runs presenter.Dispose before destroying the GameObject.
            _taskChangedSubscription?.Dispose();
            _taskChangedSubscription = null;
            _controlModeChangedSubscription?.Dispose();
            _controlModeChangedSubscription = null;

            View.CloseClicked -= OnCloseClicked;
            View.ConfirmClicked -= OnConfirmClicked;
            View.CancelClicked -= OnCancelClicked;
            View.UnwireButtons();
        }

        // ---------- build / rebuild ----------

        private void Rebuild()
        {
            _baseline.Clear();
            _drafts.Clear();
            _rowErrors.Clear();
            _conflicts.Clear();

            _knownTasks.Clear();
            var known = _stateProvider.GetKnownTaskIds();
            if (known != null)
                for (int i = 0; i < known.Count; i++) _knownTasks.Add(known[i]);

            View.ClearRows();
            _rows.Clear();
            _rowOrder.Clear();

            var disciples = _stateProvider.BuildSectEconomyState()?.Disciples;
            if (disciples == null) return;

            foreach (var d in disciples)
            {
                if (d == null) continue;

                var row = View.CreateRow();
                if (row == null) return;

                row.TaskSelected += i => OnDraftChanged(d.DiscipleId, i);

                var baselineTask = string.IsNullOrEmpty(d.CurrentTask) ? _knownTasks[0] : d.CurrentTask;
                _baseline[d.DiscipleId] = baselineTask;
                _rowOrder.Add(d.DiscipleId); // row order == disciple order (ResetDropdowns/ApplyRowErrors rely on it)

                row.SetDiscipleIdForTest(d.DiscipleId);
                row.SetName(d.DisplayName);
                row.SetBaselineTask(baselineTask);
                row.SetOwnerLabel(BuildOwnerLabel(d, baselineTask));
                // P9A — Auto is per-disciple; only Npc ownership is eligible.
                row.SetAutoState(d.ControlMode == DiscipleControlMode.Auto);
                row.SetAutoSelectable(d.OwnerType == DiscipleOwnerType.Npc);
                row.AutoToggled += wantAuto => OnAutoToggled(d.DiscipleId, wantAuto);
                ApplyRowState(row, d.DiscipleId, baselineTask);
                row.FillOptions(_knownTasks, baselineTask);

                _rows.Add(row);
            }

            View.SetConflictBanner(null);
            View.SetConfirmInteractable(false);
        }

        private static string BaseOwnerLabel(DiscipleState d)
        {
            switch (d.OwnerType)
            {
                case DiscipleOwnerType.Viewer: return "viewer: " + d.OwnerId;
                case DiscipleOwnerType.Player: return "player";
                default: return "ศิษย์สำนัก";
            }
        }

        /// <summary>
        /// P5B — render owner protection + remaining time from the read-only authority
        /// query (never inferred locally). Npc/Player rows are unchanged.
        /// </summary>
        private string BuildOwnerLabel(DiscipleState d, string baselineTask)
        {
            var label = BaseOwnerLabel(d);
            var permission = _stateProvider.CheckTaskPermission(RequesterId, d.DiscipleId, baselineTask);
            if (permission != null && permission.OwnerProtected)
                label += " · protected " + FormatRemaining(permission.OwnerProtectionRemainingSeconds);
            return label;
        }

        /// <summary>m:ss remaining in the protection window (clamped at 0).</summary>
        private static string FormatRemaining(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            var total = (int)Math.Round(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        /// <summary>Availability of a task id — same gate as TryAssignTask (no local re-implementation).</summary>
        private bool IsAvailable(string taskId, out string failReason) =>
            _stateProvider.IsTaskAvailable(taskId, out failReason);

        /// <summary>
        /// Per-row state = availability AND permission, both from the authority. P5B:
        /// a requester that may not control the disciple right now (e.g. a viewer owner
        /// still inside the protection window) gets the dropdown DISABLED with the
        /// authority's reason — the panel blocks, it does not merely label.
        /// </summary>
        private void ApplyRowState(TaskAssignmentRowCell row, string discipleId, string taskId)
        {
            if (!IsAvailable(taskId, out var reason))
            {
                row.SetBaselineStatus(false, reason);
                row.SetTaskSelectable(false);
                return;
            }

            var permission = _stateProvider.CheckTaskPermission(RequesterId, discipleId, taskId);
            if (permission != null && !permission.Allowed)
            {
                row.SetBaselineStatus(false, permission.Reason);
                row.SetTaskSelectable(false);
                return;
            }

            row.SetBaselineStatus(true, null);
            row.SetTaskSelectable(true);
        }

        /// <summary>True when the requester may control this disciple right now (authority's answer).</summary>
        private bool CanControl(string discipleId, string taskId)
        {
            var permission = _stateProvider.CheckTaskPermission(RequesterId, discipleId, taskId);
            return permission == null || permission.Allowed;
        }

        // ---------- draft selection (§5) ----------

        private void OnDraftChanged(string discipleId, int dropdownIndex)
        {
            if (dropdownIndex < 0 || dropdownIndex >= _knownTasks.Count) return;
            var taskId = _knownTasks[dropdownIndex];

            // P5B guard: the dropdown is disabled for a blocked row, but a programmatic
            // change must not slip past it either. Snap straight back to the baseline.
            if (!_baseline.TryGetValue(discipleId, out var currentBaseline) || !CanControl(discipleId, currentBaseline))
            {
                var rowIndex = _rowOrder.IndexOf(discipleId);
                if (rowIndex >= 0 && rowIndex < _rows.Count)
                {
                    var blocked = IndexOfTask(currentBaseline);
                    _rows[rowIndex].SetValueWithoutNotify(blocked < 0 ? 0 : blocked);
                }
                return;
            }

            if (_baseline.TryGetValue(discipleId, out var baselineTask) && taskId == baselineTask)
                _drafts.Remove(discipleId); // draft returned to baseline
            else
                _drafts[discipleId] = taskId;

            _rowErrors.Remove(discipleId);
            View.SetConfirmInteractable(_drafts.Count > 0);
        }

        // ---------- P9A — explicit Manual/Auto ----------

        /// <summary>
        /// Player toggled Auto for a row. The authoritative method rechecks eligibility
        /// (only Npc-owned disciples may opt into Auto), so a programmatic or stale
        /// toggle can never grant Auto to a Player/Viewer-owned disciple. On rejection
        /// the row snaps back to the authority's state and shows the reason.
        /// </summary>
        private void OnAutoToggled(string discipleId, bool wantAuto)
        {
            var mode = wantAuto ? DiscipleControlMode.Auto : DiscipleControlMode.Manual;
            var success = _stateProvider.TrySetDiscipleControlMode(discipleId, mode, out var failReason);

            var rowIndex = _rowOrder.IndexOf(discipleId);
            if (rowIndex < 0 || rowIndex >= _rows.Count) return;
            var row = _rows[rowIndex];

            // Always re-read the authority's answer — never trust the toggle's own value.
            var disciple = FindDisciple(discipleId);
            row.SetAutoState(disciple != null && disciple.ControlMode == DiscipleControlMode.Auto);

            if (success)
            {
                _baseline.TryGetValue(discipleId, out var baselineTask);
                ApplyRowState(row, discipleId, baselineTask);
            }
            else if (!string.IsNullOrEmpty(failReason))
            {
                row.SetBaselineStatus(false, failReason);
            }
        }

        private DiscipleState FindDisciple(string discipleId)
        {
            var disciples = _stateProvider.BuildSectEconomyState()?.Disciples;
            if (disciples == null) return null;
            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d != null && d.DiscipleId == discipleId) return d;
            }
            return null;
        }

        /// <summary>P9A — an external Auto→Manual (ownership switch, manual assign) updates the toggle.</summary>
        private void OnControlModeChangedExternally(DiscipleControlModeChangedMessage msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.DiscipleId)) return;
            var rowIndex = _rowOrder.IndexOf(msg.DiscipleId);
            if (rowIndex < 0 || rowIndex >= _rows.Count) return;
            _rows[rowIndex].SetAutoState(msg.NewMode == DiscipleControlMode.Auto);
        }

        private void OnCancelClicked()
        {
            // §5: Cancel changes nothing in state — drop drafts + errors + conflicts.
            _drafts.Clear();
            _rowErrors.Clear();
            _conflicts.Clear();
            ResetDropdownsToBaseline();
            View.SetConflictBanner(null);
            View.SetConfirmInteractable(false);
        }

        private void ResetDropdownsToBaseline()
        {
            for (int i = 0; i < _rows.Count && i < _rowOrder.Count; i++)
            {
                if (!_baseline.TryGetValue(_rowOrder[i], out var baselineTask)) continue;
                var row = _rows[i];
                var idx = IndexOfTask(baselineTask);
                row.SetValueWithoutNotify(idx < 0 ? 0 : idx);
                ApplyRowState(row, _rowOrder[i], baselineTask);
            }
        }

        private void OnConfirmClicked()
        {
            if (_drafts.Count == 0) return;

            // §5: not atomic — per-row TryAssignTask, per-row result.
            // Iterate a snapshot; successes remove their own draft.
            var pending = new List<KeyValuePair<string, string>>(_drafts);
            foreach (var kv in pending)
            {
                var discipleId = kv.Key;
                var draftTask = kv.Value;

                // §6: a dirty row whose baseline changed externally must be
                // re-selected before committing — never silently overwrite.
                if (_conflicts.Contains(discipleId))
                {
                    _rowErrors[discipleId] = "ขัดแย้งกับการเปลี่ยนงานจากภายนอก — เลือกงานใหม่อีกครั้งก่อนยืนยัน";
                    continue;
                }

                var success = _stateProvider.TryAssignTask(RequesterId, discipleId, draftTask, out var failReason);

                if (success)
                {
                    _drafts.Remove(discipleId);
                    _rowErrors[discipleId] = string.Empty;
                    _baseline[discipleId] = draftTask; // baseline advances with the commit
                }
                else
                {
                    _rowErrors[discipleId] = failReason; // draft retained for correction
                }
            }

            if (_drafts.Count == 0)
            {
                View.SetConflictBanner(null);
                ResetDropdownsToBaseline();
            }
            View.SetConfirmInteractable(_drafts.Count > 0);

            // surface per-row errors (row cells keep the error text via status)
            ApplyRowErrors();
        }

        private void ApplyRowErrors()
        {
            for (int i = 0; i < _rows.Count && i < _rowOrder.Count; i++)
            {
                var discipleId = _rowOrder[i];
                if (!_rowErrors.TryGetValue(discipleId, out var error)) continue;

                var row = _rows[i];
                if (string.IsNullOrEmpty(error))
                {
                    // successful commit → status shows the new baseline, and P9A: a
                    // successful manual assignment disables Auto, so re-read the mode.
                    _baseline.TryGetValue(discipleId, out var baselineTask);
                    ApplyRowState(row, discipleId, baselineTask);
                    var disciple = FindDisciple(discipleId);
                    row.SetAutoState(disciple != null && disciple.ControlMode == DiscipleControlMode.Auto);
                }
                else
                {
                    row.SetBaselineStatus(false, error);
                }
            }
        }

        // ---------- external change handling (§6) ----------

        private void OnTaskChangedExternally(DiscipleTaskChangedMessage msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.DiscipleId)) return;
            if (!_baseline.ContainsKey(msg.DiscipleId)) return; // row not shown

            _baseline[msg.DiscipleId] = msg.TaskId;

            var rowIndex = _rowOrder.IndexOf(msg.DiscipleId);
            if (rowIndex >= 0 && rowIndex < _rows.Count)
            {
                var row = _rows[rowIndex];

                if (_drafts.ContainsKey(msg.DiscipleId))
                {
                    // dirty draft + external change = conflict; draft is KEPT (not
                    // overwritten) and blocked from commit until re-selected.
                    _conflicts.Add(msg.DiscipleId);
                    row.SetBaselineStatus(false,
                        "ขัดแย้ง: งานถูกเปลี่ยนจากภายนอก — เลือกงานใหม่อีกครั้งก่อนยืนยัน");
                    View.SetConflictBanner(
                        "งานของ " + msg.DiscipleId + " ถูกเปลี่ยนจากภายนอกขณะกำลังแก้ไข — เลือกงานใหม่อีกครั้งก่อนยืนยัน");
                }
                else
                {
                    // clean row: follow the external change so the displayed
                    // selection never silently contradicts live state
                    var idx = IndexOfTask(msg.TaskId);
                    row.SetValueWithoutNotify(idx < 0 ? 0 : idx);
                    ApplyRowState(row, msg.DiscipleId, msg.TaskId);
                }
            }
        }

        /// <summary>Row order snapshot taken at rebuild — ResetDropdownsToBaseline / ApplyRowErrors rely on it.</summary>
        private readonly List<string> _rowOrder = new List<string>();

        private int IndexOfTask(string taskId)
        {
            for (int i = 0; i < _knownTasks.Count; i++)
                if (_knownTasks[i] == taskId) return i;
            return -1;
        }

        private void OnCloseClicked() => _closeCallback?.Invoke();
    }
}
