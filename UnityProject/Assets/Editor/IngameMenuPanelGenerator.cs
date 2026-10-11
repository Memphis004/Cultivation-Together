#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// P12B — in-game menu panel (Save / Return to Title + its confirmation block). Prefab built
    /// from code + appended to the MainPanelCatalog (idempotent re-run), same convention as
    /// DiscipleListPanelGenerator: shared ink/paper palette, Thai TMP font, no ContentSizeFitter
    /// on the panel root.
    ///
    /// The Return-to-Title confirmation is a block INSIDE this window (ConfirmRoot, inactive by
    /// default) instead of a second modal prefab: it is a label and two buttons, and the rule is
    /// "always ask", which needs no extra panel lifecycle. Escape is deliberately not bound.
    /// </summary>
    public static class IngameMenuPanelGenerator
    {
        private const string PrefabPath = "Assets/Prefabs/UI/IngameMenuPanel.prefab";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";
        private const string PanelId = "IngameMenu";

        [MenuItem("Xianxia/Generate IngameMenu Panel")]
        public static void Generate() => Build();

        /// <summary>Non-interactive entry (marker-file / eval path) — the build is already idempotent.</summary>
        public static void GenerateNonInteractive() => Build();

        private static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (font == null)
                Debug.LogWarning("[IngameMenuPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — labels would fall back to a font without Thai glyphs");

            var root = InkWidgets.Rect(null, "IngameMenuPanel", 0, 0, 1920, 1080);
            InkWidgets.Stretch(root);
            var backdrop = InkWidgets.Rect(root, "Backdrop", 0, 0, 1920, 1080);
            InkWidgets.Stretch(backdrop);
            InkWidgets.Fill(backdrop, UiPalette.Backdrop, true);

            var window = InkWidgets.InkPanel(root, "Window", 0, 0, 480, 360);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero;

            InkWidgets.InkPlate(window, "TitlePlate", "เมนู", font, 24, 20, 340, 56);
            var close = InkWidgets.CloseButton(window, font, 408, 20);

            var save = InkWidgets.OutlineButton(window, "SaveButton", "บันทึก", font, 80, 100, 320, 56);
            var returnToTitle = InkWidgets.OutlineButton(window, "ReturnToTitleButton", "กลับหน้าจอชื่อเกม",
                                                        font, 80, 172, 320, 56);

            var status = InkWidgets.Text(window, "StatusText", "", font, 24, 32, 240, 416, 64,
                                         UiPalette.Danger, TextAlignmentOptions.Top);
            status.textWrappingMode = TextWrappingModes.Normal;
            status.overflowMode = TextOverflowModes.Overflow;

            // ── confirmation block (inactive by default; the presenter shows it) ──
            var confirmRoot = InkWidgets.Rect(window, "ConfirmRoot", 16, 88, 448, 200);
            InkWidgets.Fill(confirmRoot, UiPalette.PaperDark, true);
            InkWidgets.Text(confirmRoot, "PromptText", "กลับหน้าจอชื่อเกม?", font, 28, 24, 12, 400, 56,
                            UiPalette.Text, TextAlignmentOptions.Center);
            var confirmYes = InkWidgets.OutlineButton(confirmRoot, "ConfirmYesButton", "ยืนยัน", font, 24, 100, 192, 56);
            var confirmNo = InkWidgets.OutlineButton(confirmRoot, "ConfirmNoButton", "ยกเลิก", font, 232, 100, 192, 56);
            confirmRoot.gameObject.SetActive(false);

            var view = root.gameObject.AddComponent<IngameMenuView>();
            var so = new SerializedObject(view);
            Set(so, "saveButton", save);
            Set(so, "returnToTitleButton", returnToTitle);
            Set(so, "closeButton", close);
            Set(so, "statusText", status);
            Set(so, "confirmRoot", confirmRoot.gameObject);
            Set(so, "confirmYesButton", confirmYes);
            Set(so, "confirmNoButton", confirmNo);
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            Object.DestroyImmediate(root.gameObject);

            UpdateCatalog(prefab);
            Debug.Log("[IngameMenuPanelGenerator] " + PanelId + " prefab + catalog entry ready at " + PrefabPath);
        }

        /// <summary>
        /// Refresh-in-place the panel's catalog entry (idempotent re-run), or append it — an
        /// existing entry keeps its array position so no other panel's index moves.
        /// </summary>
        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[IngameMenuPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }

            var cs = new SerializedObject(catalog);
            var panels = cs.FindProperty("panels");
            bool found = false;
            for (int i = 0; i < panels.arraySize; i++)
            {
                var el = panels.GetArrayElementAtIndex(i);
                if (el.FindPropertyRelative("PanelId").stringValue != PanelId) continue;
                el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                found = true;
            }

            if (!found)
            {
                panels.arraySize++;
                var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
                el.FindPropertyRelative("PanelId").stringValue = PanelId;
                el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.IngameMenu;
            }

            cs.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static void Set(SerializedObject so, string name, Object value)
            => so.FindProperty(name).objectReferenceValue = value;
    }
}
#endif
