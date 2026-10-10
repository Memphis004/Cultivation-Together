#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// E3 — the world-event popup + the "pending event" chip. Same convention as
    /// TaskAssignmentPanelGenerator / TimeControlPanelGenerator: build the prefab
    /// from code, wire serialized refs, refresh the MainPanelCatalog entry.
    ///
    /// Popup layout (why, not just what):
    ///   - root is full-canvas and carries the LIGHT DIM, whose Image has
    ///     raycastTarget=false so clicks still reach the game behind it;
    ///   - one fixed-size framed panel child (cream + darker title strip) holds the
    ///     title, the description (larger text than the title — the title is only
    ///     the event id, the Luban table has no display name), the choices, the
    ///     status line and the "ซ่อน" hide button;
    ///   - NO ContentSizeFitter anywhere (AvatarCustomization lesson) — a fitter
    ///     would resize the panel while the text changes;
    ///   - the choice button template is stored INACTIVE (that is how the prefab
    ///     file keeps a template out of the layout) and carries an Image as its
    ///     target graphic + a LayoutElement minimum height, so clones are visible,
    ///     clickable and the right size.
    /// The chip is a separate panel pinned under the E1 time bar.
    ///
    /// Re-running REPLACES the prefab assets — the menu path asks first, the
    /// remote-control path (GenerateNonInteractive) does not.
    /// </summary>
    public static class EventPopupPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs";
        private const string PrefabName = "EventPopupPrefab";
        private const string UiFolder = "Assets/Prefabs/UI";
        private const string ChipName = "EventChipPanel";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        private const float PanelWidth = 760f;
        private const float PanelHeight = 520f;
        private const float TitleStripHeight = 56f;
        private const float ChoiceMinHeight = 56f;

        private static readonly Color Cream = new Color(0.93f, 0.88f, 0.78f, 1f);
        private static readonly Color TitleBrown = new Color(0.42f, 0.30f, 0.20f, 1f);
        private static readonly Color ButtonNormal = new Color(0.85f, 0.82f, 0.76f, 1f);
        private static readonly Color ButtonHighlight = new Color(0.95f, 0.90f, 0.82f, 1f);
        private static readonly Color ButtonPressed = new Color(0.72f, 0.68f, 0.62f, 1f);
        private static readonly Color ButtonDisabled = new Color(0.72f, 0.68f, 0.62f, 0.5f);
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.35f);          // light dim
        private static readonly Color ChipPlate = new Color(0f, 0f, 0f, 0.55f);    // matches TimeControlPanel

        [MenuItem("Xianxia/Generate Event Popup Panel")]
        public static void Generate() => Build(askBeforeOverwrite: true);

        /// <summary>Non-interactive entry (GeneratorRemoteControl marker file) — no dialog can block a
        /// batch run, so the overwrite prompt is skipped.</summary>
        public static void GenerateNonInteractive() => Build(askBeforeOverwrite: false);

        private static void Build(bool askBeforeOverwrite)
        {
            string popupPath = Path.Combine(RootFolder, PrefabName + ".prefab").Replace("\\", "/");
            string chipPath = Path.Combine(UiFolder, ChipName + ".prefab").Replace("\\", "/");

            if (askBeforeOverwrite && (File.Exists(popupPath) || File.Exists(chipPath)) && !EditorUtility.DisplayDialog(
                    "Event Popup Panel",
                    "EventPopupPrefab.prefab / EventChipPanel.prefab มีอยู่แล้ว — จะเขียนทับด้วย prefab ที่ generate ใหม่?\n" +
                    "(catalog entries จะ refresh เป็น prefab ใหม่ด้วย)",
                    "Overwrite", "Cancel"))
            {
                Debug.Log("[EventPopupPanelGenerator] cancelled — existing prefabs kept");
                return;
            }

            EnsureFolder(RootFolder);
            EnsureFolder(UiFolder);

            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[EventPopupPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — text would fall back to the default font (no Thai glyphs)");

            var popup = BuildPopup(thaiFont);
            var chip = BuildChip(thaiFont);

            UpdateCatalog("EventPopup", UIPresenterKind.EventPopup, popup);
            UpdateCatalog("EventChip", UIPresenterKind.EventChip, chip);
            AssetDatabase.SaveAssets();
            Debug.Log("[EventPopupPanelGenerator] EventPopupPrefab + EventChipPanel prefabs and catalog entries ready");
        }

        // ---------------------------------------------------------------- popup

        private static GameObject BuildPopup(TMP_FontAsset font)
        {
            // ── root: full-canvas container so the dim covers the screen ──
            // (no ContentSizeFitter — checked by EventPopupLiteTests.Prefab_* )
            var root = new GameObject(PrefabName, typeof(RectTransform));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);

            // Light dim behind the panel. raycastTarget=false on purpose: the player can
            // still click the game behind the popup.
            var dim = CreateChild(root.transform, "Dim", typeof(Image));
            StretchFull(dim.GetComponent<RectTransform>());
            var dimImage = dim.GetComponent<Image>();
            dimImage.color = Dim;
            dimImage.raycastTarget = false;

            // ── framed panel ──
            var panel = CreateChild(root.transform, "Panel", typeof(Image));
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRt.anchoredPosition = Vector2.zero;
            panel.GetComponent<Image>().color = Cream;

            // ── darker title strip ──
            var strip = CreateChild(panel.transform, "TitleStrip", typeof(Image));
            var stripRt = strip.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(0f, 1f);
            stripRt.anchorMax = new Vector2(1f, 1f);
            stripRt.pivot = new Vector2(0.5f, 1f);
            stripRt.sizeDelta = new Vector2(0f, TitleStripHeight);
            stripRt.anchoredPosition = Vector2.zero;
            strip.GetComponent<Image>().color = TitleBrown;

            var title = CreateText(strip.transform, "TitleText", "Event Title", font);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(16f, 0f);
            titleRt.offsetMax = new Vector2(-140f, 0f);
            title.color = UiPalette.LightText;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.fontSize = 28;   // the id only — the description carries the content

            var hideButton = CreateButton(strip.transform, "HideButton", "ซ่อน", font);
            var hideRt = hideButton.GetComponent<RectTransform>();
            hideRt.anchorMin = hideRt.anchorMax = new Vector2(1f, 0.5f);
            hideRt.pivot = new Vector2(1f, 0.5f);
            hideRt.sizeDelta = new Vector2(110f, 36f);
            hideRt.anchoredPosition = new Vector2(-10f, 0f);

            var description = CreateText(panel.transform, "DescriptionText", string.Empty, font);
            var descRt = description.rectTransform;
            descRt.anchorMin = new Vector2(0f, 1f);
            descRt.anchorMax = new Vector2(1f, 1f);
            descRt.pivot = new Vector2(0.5f, 1f);
            descRt.sizeDelta = new Vector2(-40f, 150f);
            descRt.anchoredPosition = new Vector2(0f, -(TitleStripHeight + 10f));
            description.color = UiPalette.Text;
            description.alignment = TextAlignmentOptions.TopLeft;
            description.fontSize = 34;   // larger than the title (spec §2)

            // ── choices (runtime-built clones) ──
            var choicesRoot = CreateChild(panel.transform, "ChoicesRoot",
                typeof(RectTransform), typeof(VerticalLayoutGroup));
            var choicesRt = choicesRoot.GetComponent<RectTransform>();
            choicesRt.anchorMin = new Vector2(0f, 0f);
            choicesRt.anchorMax = new Vector2(1f, 1f);
            choicesRt.offsetMin = new Vector2(20f, 56f);
            choicesRt.offsetMax = new Vector2(-20f, -(TitleStripHeight + 10f + 150f));
            var vlg = choicesRoot.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Template: INACTIVE (kept out of the layout), Image on the button itself so
            // the clone has a visible, tintable target graphic, LayoutElement so every
            // row is at least ChoiceMinHeight tall.
            var template = CreateChild(panel.transform, "ChoiceButtonTemplate",
                typeof(Image), typeof(Button), typeof(LayoutElement));
            var templateRt = template.GetComponent<RectTransform>();
            templateRt.anchorMin = templateRt.anchorMax = new Vector2(0.5f, 0.5f);
            templateRt.pivot = new Vector2(0.5f, 0.5f);
            templateRt.sizeDelta = new Vector2(PanelWidth - 40f, ChoiceMinHeight);
            var templateImage = template.GetComponent<Image>();
            templateImage.color = ButtonNormal;
            var templateButton = template.GetComponent<Button>();
            templateButton.targetGraphic = templateImage;
            templateButton.colors = Tint(ButtonNormal, ButtonHighlight, ButtonPressed, ButtonDisabled);
            var templateLayout = template.GetComponent<LayoutElement>();
            templateLayout.minHeight = ChoiceMinHeight;
            templateLayout.preferredHeight = ChoiceMinHeight;
            var templateLabel = CreateText(template.transform, "Label", "Choice", font);
            StretchFull(templateLabel.rectTransform);
            templateLabel.color = UiPalette.Text;
            templateLabel.alignment = TextAlignmentOptions.Center;
            templateLabel.fontSize = 28;
            template.SetActive(false);

            var status = CreateText(panel.transform, "StatusText", string.Empty, font);
            var statusRt = status.rectTransform;
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(1f, 0f);
            statusRt.pivot = new Vector2(0.5f, 0f);
            statusRt.sizeDelta = new Vector2(-40f, 40f);
            statusRt.anchoredPosition = new Vector2(0f, 8f);
            status.color = UiPalette.Danger;
            status.alignment = TextAlignmentOptions.MidlineLeft;
            status.fontSize = 26;

            var view = root.AddComponent<EventPopupView>();
            var so = new SerializedObject(view);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("descriptionText").objectReferenceValue = description;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("choicesRoot").objectReferenceValue = choicesRt;
            so.FindProperty("choiceButtonPrefab").objectReferenceValue = templateButton;
            so.FindProperty("hideButton").objectReferenceValue = hideButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, RootFolder, PrefabName);
        }

        // ---------------------------------------------------------------- chip

        private static GameObject BuildChip(TMP_FontAsset font)
        {
            var root = new GameObject(ChipName, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = root.GetComponent<RectTransform>();
            // E0 free region: directly below the E1 time bar (560x56 at (12,-110)).
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(400f, 48f);
            rt.anchoredPosition = new Vector2(12f, -176f);

            var background = root.GetComponent<Image>();
            background.color = ChipPlate;

            var button = root.GetComponent<Button>();
            button.targetGraphic = background;
            button.colors = Tint(ChipPlate, ButtonHighlight, ButtonPressed, ButtonDisabled);

            var label = CreateText(root.transform, "Label", "มีเหตุการณ์รอตัดสินใจ", font);
            StretchFull(label.rectTransform);
            label.color = UiPalette.LightText;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 26;

            var view = root.AddComponent<WorldEventChipView>();
            var so = new SerializedObject(view);
            so.FindProperty("chipButton").objectReferenceValue = button;
            so.FindProperty("background").objectReferenceValue = background;
            so.FindProperty("label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, UiFolder, ChipName);
        }

        // ---------------------------------------------------------------- shared

        private static ColorBlock Tint(Color normal, Color highlighted, Color pressed, Color disabled)
        {
            var colors = new ColorBlock();
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = highlighted;
            colors.disabledColor = disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            return colors;
        }

        /// <summary>Refresh (delete + re-append) the catalog entry so a re-run stays idempotent.</summary>
        private static void UpdateCatalog(string panelId, UIPresenterKind kind, GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[EventPopupPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }

            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == panelId)
                {
                    panels.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = panelId;
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateChild(Transform parent, string name, params System.Type[] components)
        {
            var all = new System.Type[components.Length + 1];
            all[0] = typeof(RectTransform);
            components.CopyTo(all, 1);
            var go = new GameObject(name, all);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, TMP_FontAsset font)
        {
            var go = CreateChild(parent, name, typeof(TextMeshProUGUI));
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null) text.font = font;
            text.color = UiPalette.Text;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font)
        {
            var go = CreateChild(parent, name, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>();
            img.color = ButtonNormal;
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.colors = Tint(ButtonNormal, ButtonHighlight, ButtonPressed, ButtonDisabled);

            var text = CreateText(go.transform, "Label", label, font);
            StretchFull(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.color = UiPalette.Text;
            text.fontSize = 26;
            return button;
        }

        private static GameObject SavePrefab(GameObject go, string folder, string prefabName)
        {
            string path = Path.Combine(folder, prefabName + ".prefab").Replace("\\", "/");
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string folderName = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif
