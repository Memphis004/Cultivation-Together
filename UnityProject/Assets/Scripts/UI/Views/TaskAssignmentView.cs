using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>Open args for the task assignment panel — close callback pattern
    /// เดียวกับ ResourcePopupArgs/DiscipleListArgs/BuildingMenuArgs (opener เป็นคนปิด).</summary>
    public sealed class TaskAssignmentArgs
    {
        public System.Action CloseCallback;
    }

    /// <summary>
    /// 1 แถวต่อศิษย์: ชื่อ + งานปัจจุบัน (baseline) + ownership label +
    /// availability ของงาน baseline + dropdown เลือกงานใหม่ + เหตุผลเมื่อไม่ available.
    /// เหมือน BuildingItemCell — สร้างจากโค้ด, ปุ่ม/ตัวเลือก wire ใน Init,
    /// "public Click()" ไว้ให้ test เรียกตรง (C12 — ไม่ใช้ reflection).
    /// </summary>
    public sealed class TaskAssignmentRowCell : MonoBehaviour
    {
        private TMP_Text _nameText;
        private TMP_Text _baselineText;
        private TMP_Text _ownerText;
        private TMP_Text _statusText;
        private TMP_Dropdown _taskDropdown;

        public event Action<int> TaskSelected; // dropdown index

        public TMP_Dropdown Dropdown => _taskDropdown;

        public void Init(TMP_Text nameText, TMP_Text baselineText, TMP_Text ownerText,
                         TMP_Text statusText, TMP_Dropdown taskDropdown)
        {
            _nameText = nameText;
            _baselineText = baselineText;
            _ownerText = ownerText;
            _statusText = statusText;
            _taskDropdown = taskDropdown;

            if (_taskDropdown != null)
                _taskDropdown.onValueChanged.AddListener(OnDropdownChanged);
        }

        public void SetName(string name) { if (_nameText != null) _nameText.text = name; }
        public void SetBaselineTask(string taskId) { if (_baselineText != null) _baselineText.text = taskId; }
        public void SetOwnerLabel(string label) { if (_ownerText != null) _ownerText.text = label; }

        /// <summary>Test seam — the row remembers which disciple it renders.</summary>
        public void SetDiscipleIdForTest(string discipleId) => DiscipleIdForTest = discipleId;

        /// <summary>สถานะ baseline: "✓ available" หรือ "✗ unavailable — reason"</summary>
        public void SetBaselineStatus(bool available, string reason)
        {
            if (_statusText == null) return;
            _statusText.text = available ? "✓" : ("✗ " + (reason ?? string.Empty));
            _statusText.color = available ? new Color(0.2f, 0.55f, 0.2f) : new Color(0.75f, 0.2f, 0.2f);
        }

        public void FillOptions(IReadOnlyList<string> taskIds, string baselineTaskId)
        {
            if (_taskDropdown == null) return;
            _taskDropdown.ClearOptions();
            var options = new List<TMP_Dropdown.OptionData>(taskIds.Count);
            for (int i = 0; i < taskIds.Count; i++)
                options.Add(new TMP_Dropdown.OptionData(taskIds[i]));
            _taskDropdown.AddOptions(options);

            // baseline = selection เริ่มต้น (draft == baseline ตอนเปิดแถว)
            int idx = -1;
            for (int i = 0; i < taskIds.Count; i++)
            {
                if (taskIds[i] == baselineTaskId) { idx = i; break; }
            }
            _taskDropdown.SetValueWithoutNotify(idx < 0 ? 0 : idx);
        }

        public void SetValueWithoutNotify(int index)
        {
            if (_taskDropdown != null) _taskDropdown.SetValueWithoutNotify(index);
        }

        /// <summary>
        /// P5B — the authority says this requester may not change this disciple's task
        /// right now (e.g. a viewer owner is still inside the protection window), so
        /// the dropdown is disabled rather than merely labelled.
        /// </summary>
        public void SetTaskSelectable(bool selectable)
        {
            if (_taskDropdown != null) _taskDropdown.interactable = selectable;
        }

        /// <summary>Public on purpose: tests invoke directly (no reflection — C12).
        /// Mirrors real user interaction: the dropdown value changes, THEN the event fires.</summary>
        public void SelectTask(int index)
        {
            if (_taskDropdown != null) _taskDropdown.SetValueWithoutNotify(index);
            OnDropdownChanged(index);
        }

        private void OnDropdownChanged(int index) => TaskSelected?.Invoke(index);

        private void OnDestroy()
        {
            if (_taskDropdown != null) _taskDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
            TaskSelected = null;
        }

        // ---- test accessors (EditMode tests drive rows without reflection — C12) ----
        public string DiscipleIdForTest { get; private set; }
        public string DisplayNameForTest => _nameText != null ? _nameText.text : null;
        public string OwnerLabelForTest => _ownerText != null ? _ownerText.text : null;
        public string StatusTextForTest => _statusText != null ? _statusText.text : null;
        public bool IsTaskSelectableForTest => _taskDropdown == null || _taskDropdown.interactable;
        public string DropdownValueText => _taskDropdown != null && _taskDropdown.options.Count > 0
            ? _taskDropdown.options[Mathf.Clamp(_taskDropdown.value, 0, _taskDropdown.options.Count - 1)].text
            : null;
        public List<string> OptionsForTest
        {
            get
            {
                var list = new List<string>();
                if (_taskDropdown != null)
                    foreach (var opt in _taskDropdown.options) list.Add(opt.text);
                return list;
            }
        }
    }

    /// <summary>
    /// Task assignment panel (P3): ตาราง 1 แถว/ศิษย์ + Confirm/Cancel.
    /// โครงหลัก (title/close/rowRoot/confirm/cancel/conflictBanner) wire จาก
    /// prefab/generator; แถวทั้งหมดสร้าง runtime ใน CreateRow (แพทเทิร์นเดียวกับ
    /// BuildingMenuView.CreateItem). Modal → backdrop full-screen มาจาก generator.
    /// </summary>
    public class TaskAssignmentView : UIViewBase
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text conflictBanner;
        [SerializeField] private RectTransform rowRoot;
        [SerializeField] private ScrollRect scrollRect;

        public event Action CloseClicked;
        public event Action ConfirmClicked;
        public event Action CancelClicked;

        public RectTransform RowRoot => rowRoot;

        private readonly List<TaskAssignmentRowCell> _liveRows = new List<TaskAssignmentRowCell>();

        /// <summary>Test accessor — the live rows exactly as ClearRows/CreateRow maintain them.
        /// (Iterating rowRoot children in EditMode sees pending-Destroy rows from a previous build.)</summary>
        public IReadOnlyList<TaskAssignmentRowCell> LiveRowsForTest => _liveRows;

        /// <summary>Rows are runtime-built (ResourcePopupView.CreateRow pattern — never in the prefab).</summary>
        public TaskAssignmentRowCell CreateRow()
        {
            if (rowRoot == null) return null;

            var go = new GameObject("TaskRow",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(rowRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;

            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 56f;

            var bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.5f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = bg;
            button.interactable = false; // visual row; dropdown is the interaction

            var nameText = CreateRowText(go.transform, "NameText", 220f);
            var baselineText = CreateRowText(go.transform, "BaselineText", 130f);
            var ownerText = CreateRowText(go.transform, "OwnerText", 120f);
            var statusText = CreateRowText(go.transform, "StatusText", 220f);

            var dropdownGo = new GameObject("TaskDropdown",
                typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
            dropdownGo.transform.SetParent(go.transform, false);
            var ddRt = dropdownGo.GetComponent<RectTransform>();
            ddRt.anchorMin = new Vector2(1f, 0.5f);
            ddRt.anchorMax = new Vector2(1f, 0.5f);
            ddRt.pivot = new Vector2(1f, 0.5f);
            ddRt.sizeDelta = new Vector2(160f, 40f);
            ddRt.anchoredPosition = new Vector2(-8f, 0f);
            var ddImg = dropdownGo.GetComponent<Image>();
            ddImg.color = new Color(0.85f, 0.85f, 0.85f);
            var dropdown = dropdownGo.GetComponent<TMP_Dropdown>();
            dropdown.targetGraphic = ddImg;
            dropdown.captionText = CreateRowText(dropdownGo.transform, "Caption", 130f, centered: true);

            var cell = go.AddComponent<TaskAssignmentRowCell>();
            cell.Init(nameText, baselineText, ownerText, statusText, dropdown);
            _liveRows.Add(cell);
            return cell;
        }

        private static TMP_Text CreateRowText(Transform parent, string name, float width, bool centered = false)
        {
            var textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(parent, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, 28f);
            rt.anchoredPosition = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = string.Empty;
            tmp.color = Color.black;
            tmp.fontSize = 22;
            tmp.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>Removes runtime rows (called by the presenter's Rebuild on every open).
        /// DestroyImmediate — this panel is rebuilt in EditMode tests too, where
        /// deferred Destroy logs "Destroy may not be called from edit mode".</summary>
        public void ClearRows()
        {
            for (int i = _liveRows.Count - 1; i >= 0; i--)
            {
                var row = _liveRows[i];
                if (row != null) DestroyImmediate(row.gameObject);
            }
            _liveRows.Clear();
        }

        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            // centered modal — กว้างกว่า BuildingMenu นิดหน่อยเพราะแถวมี 5 คอลัมน์
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(760f, 640f);
            rt.anchoredPosition = Vector2.zero;
        }

        public void SetConflictBanner(string message)
        {
            if (conflictBanner == null) return;
            if (string.IsNullOrEmpty(message))
            {
                conflictBanner.gameObject.SetActive(false);
            }
            else
            {
                conflictBanner.gameObject.SetActive(true);
                conflictBanner.text = message;
                conflictBanner.color = new Color(0.75f, 0.2f, 0.2f);
            }
        }

        public void SetConfirmInteractable(bool interactable)
        {
            if (confirmButton != null) confirmButton.interactable = interactable;
        }

        // ---- test accessors (EditMode tests, no reflection — C12) ----
        public bool IsConfirmInteractableForTest => confirmButton != null && confirmButton.interactable;
        public string ConflictBannerForTest =>
            (conflictBanner != null && conflictBanner.gameObject.activeSelf) ? conflictBanner.text : string.Empty;

        /// <summary>Test hook — mirrors ConfirmClicked for tests that drive the presenter directly.</summary>
        public void InvokeConfirmForTest() => ConfirmClicked?.Invoke();

        /// <summary>Test hook — mirrors CancelClicked for tests that drive the presenter directly.</summary>
        public void InvokeCancelForTest() => CancelClicked?.Invoke();

        private bool _closeWired;

        public override void Show()
        {
            EnsureCloseWired();
            gameObject.SetActive(true);
        }

        private void EnsureCloseWired()
        {
            if (_closeWired || closeButton == null) return;
            closeButton.onClick.AddListener(OnCloseClicked);
            _closeWired = true;
        }

        public void WireButtons()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
        }

        public void UnwireButtons()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(OnCancelClicked);
        }

        private void OnDestroy()
        {
            if (closeButton != null && _closeWired)
                closeButton.onClick.RemoveListener(OnCloseClicked);
            _closeWired = false;
            UnwireButtons();
            CloseClicked = null;
            ConfirmClicked = null;
            CancelClicked = null;
        }

        private void OnCloseClicked() => CloseClicked?.Invoke();
        private void OnConfirmClicked() => ConfirmClicked?.Invoke();
        private void OnCancelClicked() => CancelClicked?.Invoke();
    }
}
