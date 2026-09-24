using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // One row inside the resource popup: label + value. Rows for resources
    // the economy does not track yet are shown greyed-out (dimmed alpha)
    // instead of being hidden, so the layout matches the reference art
    // without inventing data.
    // Rows are built entirely in code (see ResourcePopupView.CreateRow);
    // nothing here is serialized, so the prefab cannot desync from the view.
    public sealed class ResourcePopupRow : MonoBehaviour
    {
        private const float EnabledAlpha = 1f;
        private const float DisabledAlpha = 0.35f;

        private TMP_Text labelText;
        private TMP_Text valueText;
        private CanvasGroup canvasGroup;

        public void Init(TMP_Text label, TMP_Text value, CanvasGroup group)
        {
            labelText = label;
            valueText = value;
            canvasGroup = group;
        }

        public void Set(string label, string value, bool enabled)
        {
            if (labelText != null) labelText.text = label;
            if (valueText != null) valueText.text = value;
            if (canvasGroup != null) canvasGroup.alpha = enabled ? EnabledAlpha : DisabledAlpha;
        }

        public void SetValue(string value)
        {
            if (valueText != null) valueText.text = value;
        }

        // Read accessors (verification/debug tools; presenter uses Set/SetValue).
        public string Label => labelText != null ? labelText.text : null;
        public string Value => valueText != null ? valueText.text : null;
        public float Alpha => canvasGroup != null ? canvasGroup.alpha : 1f;
    }

    // Resource popup ("คลังสินค้า"): read-only list of the sect stockpile.
    // Only titleText / closeButton / rowsRoot are wired on the prefab; the
    // rows themselves are constructed at runtime in CreateRow so the layout
    // cannot be lost to a stale prefab import.
    public class ResourcePopupView : UIViewBase
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform rowsRoot;

        public event Action CloseClicked;

        public TMP_Text TitleText => titleText;
        public Transform RowsRoot => rowsRoot;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(OnCloseClicked);

            EnsureRowsLayout();
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnCloseClicked);
            CloseClicked = null;
        }

        private void OnCloseClicked() => CloseClicked?.Invoke();

        // The prefab normally provides this; add it defensively so the popup
        // still stacks rows correctly if the component was dropped on import.
        private void EnsureRowsLayout()
        {
            if (rowsRoot == null) return;

            var layout = rowsRoot.GetComponent<VerticalLayoutGroup>();
            if (layout != null) return;

            layout = rowsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        public ResourcePopupRow CreateRow()
        {
            if (rowsRoot == null)
            {
                Debug.LogError("[ResourcePopupView] rowsRoot not wired on prefab");
                return null;
            }

            // Build the row entirely in code so it cannot desync from the
            // prefab file (earlier prefab-serialized template kept getting
            // lost/stale across imports).
            var rowGo = new GameObject("Row", typeof(RectTransform));
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.SetParent(rowsRoot, false);

            var canvasGroup = rowGo.AddComponent<CanvasGroup>();
            var row = rowGo.AddComponent<ResourcePopupRow>();

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 12f;
            hlg.padding = new RectOffset(8, 8, 0, 0);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var rowElement = rowGo.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 40f;

            // Thai-capable font inherited from the popup title.
            var font = titleText != null ? titleText.font : null;

            var labelText = CreateText("Label", font, 30f, TextAlignmentOptions.Left, rowRect);
            var labelElement = labelText.gameObject.AddComponent<LayoutElement>();
            labelElement.flexibleWidth = 1f;

            var valueText = CreateText("Value", font, 30f, TextAlignmentOptions.Right, rowRect);
            var valueElement = valueText.gameObject.AddComponent<LayoutElement>();
            valueElement.preferredWidth = 140f;

            row.Init(labelText, valueText, canvasGroup);
            return row;
        }

        private static TMP_Text CreateText(
            string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        public void ClearRows()
        {
            if (rowsRoot == null) return;

            for (var i = rowsRoot.childCount - 1; i >= 0; i--)
                Destroy(rowsRoot.GetChild(i).gameObject);
        }
    }
}
