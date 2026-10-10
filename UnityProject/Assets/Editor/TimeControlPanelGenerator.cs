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
    /// E1.1 — TimeControl HUD panel. Builds the prefab from code + appends it to the
    /// MainPanelCatalog (same convention as TaskAssignmentPanelGenerator /
    /// BuildingMenuPanelGenerator). Layout: top-left, just below the WalletHud bar.
    /// Labels use THSarabunPSK SDF (the project's Thai font). The Pause label is a
    /// plain Thai word ("หยุด") — NOT a ▶/⏸ symbol — because the Thai font's coverage
    /// of those glyphs is unverified, and a missing glyph renders as a square.
    ///
    /// E1.1 removed the Play button: a speed button already means "play at Nx", so
    /// Play was a second control for the same state (and its highlight could only
    /// ever show a state the speed row could not). The remaining four buttons slot
    /// left into the freed 76px, keeping the SAME gaps (6 / 4 / 4 / 8) and the same
    /// 8px margins, so the row rhythm is unchanged; the status label takes the freed
    /// width (226 → 302) and stays on the same baseline.
    /// Re-running REPLACES the existing prefab asset — the menu asks first, the
    /// remote-control path (GenerateNonInteractive) does not.
    /// </summary>
    public static class TimeControlPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string PrefabName = "TimeControlPanel";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        [MenuItem("Xianxia/Generate TimeControl Panel")]
        public static void Generate() => Build(askBeforeOverwrite: true);

        /// <summary>Non-interactive entry (GeneratorRemoteControl marker file) — no dialog can block a
        /// batch run, so the overwrite prompt is skipped.</summary>
        public static void GenerateNonInteractive() => Build(askBeforeOverwrite: false);

        private static void Build(bool askBeforeOverwrite)
        {
            string path = Path.Combine(RootFolder, PrefabName + ".prefab").Replace("\\", "/");
            if (askBeforeOverwrite && File.Exists(path) && !EditorUtility.DisplayDialog(
                    "TimeControl Panel",
                    "Assets/Prefabs/UI/TimeControlPanel.prefab มีอยู่แล้ว — จะเขียนทับด้วย prefab ที่ generate ใหม่?\n" +
                    "(catalog entry จะ refresh เป็น prefab ใหม่ด้วย)",
                    "Overwrite", "Cancel"))
            {
                Debug.Log("[TimeControlPanelGenerator] cancelled — existing prefab kept");
                return;
            }

            EnsureFolder(RootFolder);
            var prefab = BuildPanel();
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[TimeControlPanelGenerator] TimeControlPanel prefab + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel()
        {
            var root = new GameObject(PrefabName, typeof(RectTransform), typeof(Image));
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0f, 1f);
            rootRt.anchorMax = new Vector2(0f, 1f);
            rootRt.pivot = new Vector2(0f, 1f);
            rootRt.sizeDelta = new Vector2(560f, 56f);
            rootRt.anchoredPosition = new Vector2(12f, -110f);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[TimeControlPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — labels would fall back to the default font (no Thai glyphs)");

            // E1.1 layout: pause 8..78, 6 gap, 1x 84..134, 4 gap, 2x 138..188,
            // 4 gap, 3x 192..242, 8 gap, status 250..552 (8px right margin).
            var pauseButton = CreateButton(root.transform, "PauseButton", "หยุด", thaiFont, 8f, 70f);
            var speed1 = CreateButton(root.transform, "Speed1Button", "1x", thaiFont, 84f, 50f);
            var speed2 = CreateButton(root.transform, "Speed2Button", "2x", thaiFont, 138f, 50f);
            var speed3 = CreateButton(root.transform, "Speed3Button", "3x", thaiFont, 192f, 50f);

            var status = CreateText(root.transform, "StatusText", "กำลังเดิน 1x", thaiFont);
            var statusRt = status.rectTransform;
            statusRt.anchorMin = new Vector2(0f, 0.5f);
            statusRt.anchorMax = new Vector2(0f, 0.5f);
            statusRt.pivot = new Vector2(0f, 0.5f);
            statusRt.sizeDelta = new Vector2(302f, 40f);
            statusRt.anchoredPosition = new Vector2(250f, 0f);
            status.alignment = TextAlignmentOptions.MidlineLeft;
            status.fontSize = 24;

            var view = root.AddComponent<TimeControlView>();
            var so = new SerializedObject(view);
            so.FindProperty("pauseButton").objectReferenceValue = pauseButton;
            so.FindProperty("speed1Button").objectReferenceValue = speed1;
            so.FindProperty("speed2Button").objectReferenceValue = speed2;
            so.FindProperty("speed3Button").objectReferenceValue = speed3;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, PrefabName);
        }

        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[TimeControlPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }
            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == "TimeControl")
                {
                    panels.DeleteArrayElementAtIndex(i); // refresh the entry (idempotent re-run)
                    break;
                }
            }
            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = "TimeControl";
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.TimeControl;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button CreateButton(Transform parent, string name, string label,
                                           TMP_FontAsset font, float x, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, 40f);
            rt.anchoredPosition = new Vector2(x, 0f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.80f, 0.80f, 0.80f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;

            var text = CreateText(go.transform, "Label", label, font);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 24;
            return button;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null) text.font = font;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject SavePrefab(GameObject go, string prefabName)
        {
            string path = Path.Combine(RootFolder, prefabName + ".prefab").Replace("\\", "/");
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
