#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// P12B — Title screen panel. Prefab built from code + appended to the
    /// MainPanelCatalog (idempotent re-run), same convention as
    /// DiscipleListPanelGenerator / DiscipleDetailPanelGenerator: shared ink/paper palette
    /// (<see cref="UiPalette"/> / <see cref="InkWidgets"/>), Thai TMP font
    /// (THSarabunPSK SDF), and NO ContentSizeFitter on the panel root.
    ///
    /// The panel owns its own opaque full-screen background on purpose: the core scene's
    /// Camera GameObject is inactive and the gameplay backdrop lives in the additive gameplay
    /// scene, which is UNLOADED while the phase is Title — so a Title that relied on a camera
    /// would have nothing behind it (see the P12B survey note). Being camera-independent also
    /// means the screen is identical in the Editor and in a build.
    /// </summary>
    public static class TitleMenuPanelGenerator
    {
        private const string PrefabPath = "Assets/Prefabs/UI/TitleMenuPanel.prefab";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";
        private const string PanelId = "TitleMenu";

        [MenuItem("Xianxia/Generate TitleMenu Panel")]
        public static void Generate() => Build();

        /// <summary>Non-interactive entry (marker-file / eval path) — the build is already idempotent.</summary>
        public static void GenerateNonInteractive() => Build();

        private static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (font == null)
                Debug.LogWarning("[TitleMenuPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — labels would fall back to a font without Thai glyphs");

            // ── root: the whole screen, opaque ink (the panel IS the title background) ──
            var root = InkWidgets.Rect(null, "TitleMenuPanel", 0, 0, 1920, 1080);
            InkWidgets.Stretch(root);
            InkWidgets.Fill(root, UiPalette.Ink, true); // raycast: clicks must not fall through

            var window = InkWidgets.InkPanel(root, "Window", 0, 0, 560, 420);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero;

            var plate = InkWidgets.InkPlate(window, "TitlePlate", "สำนักเซียน", font, 40, 24, 480, 56);

            var newGame = InkWidgets.OutlineButton(window, "NewGameButton", "เล่นใหม่", font, 120, 110, 320, 56);
            var continueButton = InkWidgets.OutlineButton(window, "ContinueButton", "เล่นต่อ", font, 120, 182, 320, 56);
            var quit = InkWidgets.OutlineButton(window, "QuitButton", "ออกจากเกม", font, 120, 254, 320, 56);

            // The status line carries the save errors (missing / corrupt / unsupported / I/O), so it
            // wraps instead of ellipsizing — a truncated reason is a hidden reason (§2).
            var status = InkWidgets.Text(window, "StatusText", "", font, 24, 40, 330, 480, 72,
                                         UiPalette.Danger, TextAlignmentOptions.Top);
            status.textWrappingMode = TextWrappingModes.Normal;
            status.overflowMode = TextOverflowModes.Overflow;

            var view = root.gameObject.AddComponent<TitleMenuView>();
            var so = new SerializedObject(view);
            Set(so, "newGameButton", newGame);
            Set(so, "continueButton", continueButton);
            Set(so, "quitButton", quit);
            Set(so, "statusText", status);
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            Object.DestroyImmediate(root.gameObject);

            UpdateCatalog(prefab);
            Debug.Log("[TitleMenuPanelGenerator] " + PanelId + " prefab + catalog entry ready at " + PrefabPath);
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
                Debug.LogError("[TitleMenuPanelGenerator] catalog asset not found: " + CatalogPath);
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
                el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.TitleMenu;
            }

            cs.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static void Set(SerializedObject so, string name, Object value)
            => so.FindProperty(name).objectReferenceValue = value;
    }
}
#endif
