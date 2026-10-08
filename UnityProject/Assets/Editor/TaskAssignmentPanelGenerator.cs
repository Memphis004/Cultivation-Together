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
    /// TaskAssignment panel — builds the prefab from code + appends it to the
    /// MainPanelCatalog (same convention as BuildingMenuPanelGenerator /
    /// DiscipleListPanelGenerator). Rows are runtime-built by
    /// TaskAssignmentView.CreateRow — the prefab carries only the shell
    /// (backdrop / window / title / close / confirm / cancel / conflict banner /
    /// scroll+rowRoot). titleText ใช้ THSarabunPSK SDF เหมือนทุก panel.
    /// Re-running REPLACES the existing prefab asset — asks first (P3 §7).
    /// </summary>
    public static class TaskAssignmentPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string PrefabName = "TaskAssignmentPanel";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        [MenuItem("Xianxia/Generate TaskAssignment Panel")]
        public static void Generate()
        {
            string path = Path.Combine(RootFolder, PrefabName + ".prefab").Replace("\\", "/");
            if (File.Exists(path) && !EditorUtility.DisplayDialog(
                    "TaskAssignment Panel",
                    "Assets/Prefabs/UI/TaskAssignmentPanel.prefab มีอยู่แล้ว — จะเขียนทับด้วย prefab ที่ generate ใหม่?\n" +
                    "(catalog entry จะ refresh เป็น prefab ใหม่ด้วย)",
                    "Overwrite", "Cancel"))
            {
                Debug.Log("[TaskAssignmentPanelGenerator] cancelled — existing prefab kept");
                return;
            }

            EnsureFolder(RootFolder);
            var prefab = BuildPanel();
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[TaskAssignmentPanelGenerator] TaskAssignmentPanel prefab + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel()
        {
            // ── root ── (no ContentSizeFitter at root — AvatarCustomization lesson)
            var root = new GameObject(PrefabName, typeof(RectTransform), typeof(CanvasGroup));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);

            // modal → full-screen backdrop (req §7)
            var backdrop = CreateChild(root.transform, "Backdrop", typeof(Image));
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var window = CreateChild(root.transform, "Window", typeof(Image));
            var windowRt = window.GetComponent<RectTransform>();
            windowRt.anchorMin = windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            windowRt.pivot = new Vector2(0.5f, 0.5f);
            windowRt.sizeDelta = new Vector2(760f, 640f);
            windowRt.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.93f, 0.88f, 0.78f, 1f);

            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[TaskAssignmentPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — text would fall back to the default font (no Thai glyphs)");

            // Title
            var title = CreateText(window.transform, "TitleText", "มอบหมายงานศิษย์", thaiFont);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(-260f, 50f);
            titleRt.anchoredPosition = new Vector2(8f, -8f);
            title.fontSize = 34;

            // Close button (top-right)
            var closeButton = CreateButton(window.transform, "CloseButton", "ปิด", thaiFont);
            var closeRt = closeButton.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f); closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(110f, 40f);
            closeRt.anchoredPosition = new Vector2(-12f, -8f);

            // Conflict banner (under the title, hidden until a conflict fires)
            var bannerGo = CreateChild(window.transform, "ConflictBanner", typeof(TextMeshProUGUI));
            var bannerRt = bannerGo.GetComponent<RectTransform>();
            bannerRt.anchorMin = new Vector2(0f, 1f); bannerRt.anchorMax = new Vector2(1f, 1f);
            bannerRt.pivot = new Vector2(0.5f, 1f);
            bannerRt.sizeDelta = new Vector2(-16f, 40f);
            bannerRt.anchoredPosition = new Vector2(0f, -62f);
            var banner = bannerGo.GetComponent<TextMeshProUGUI>();
            banner.text = string.Empty;
            banner.color = new Color(0.75f, 0.2f, 0.2f);
            banner.fontSize = 22;
            banner.alignment = TextAlignmentOptions.Left;
            banner.raycastTarget = false;
            if (thaiFont != null) banner.font = thaiFont;
            bannerGo.SetActive(false);

            // Confirm / Cancel (bottom bar)
            var confirmButton = CreateButton(window.transform, "ConfirmButton", "ยืนยัน", thaiFont);
            var confirmRt = confirmButton.GetComponent<RectTransform>();
            confirmRt.anchorMin = new Vector2(1f, 0f); confirmRt.anchorMax = new Vector2(1f, 0f);
            confirmRt.pivot = new Vector2(1f, 0f);
            confirmRt.sizeDelta = new Vector2(140f, 44f);
            confirmRt.anchoredPosition = new Vector2(-12f, 8f);

            var cancelButton = CreateButton(window.transform, "CancelButton", "ยกเลิก", thaiFont);
            var cancelRt = cancelButton.GetComponent<RectTransform>();
            cancelRt.anchorMin = new Vector2(1f, 0f); cancelRt.anchorMax = new Vector2(1f, 0f);
            cancelRt.pivot = new Vector2(1f, 0f);
            cancelRt.sizeDelta = new Vector2(120f, 44f);
            cancelRt.anchoredPosition = new Vector2(-160f, 8f);

            // Scroll view (rows are runtime-built by TaskAssignmentView.CreateRow)
            var scrollView = CreateChild(window.transform, "ScrollView",
                typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
            var scrollLe = scrollView.GetComponent<LayoutElement>();
            scrollLe.ignoreLayout = true;
            var scrollRt = scrollView.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f); scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.pivot = new Vector2(0.5f, 0.5f);
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -116f);
            scrollView.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            scrollView.GetComponent<Mask>().showMaskGraphic = false;
            var scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            var viewport = CreateChild(scrollView.transform, "Viewport", typeof(Image), typeof(Mask));
            StretchFull(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewport.GetComponent<RectTransform>();

            var rowRoot = CreateChild(viewport.transform, "Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var rowRt = rowRoot.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.anchoredPosition = Vector2.zero;
            rowRt.sizeDelta = new Vector2(0f, 0f);
            var vlg = rowRoot.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            scrollRect.content = rowRt;

            // View component + serialized refs
            var view = root.AddComponent<TaskAssignmentView>();
            var so = new SerializedObject(view);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("confirmButton").objectReferenceValue = confirmButton;
            so.FindProperty("cancelButton").objectReferenceValue = cancelButton;
            so.FindProperty("conflictBanner").objectReferenceValue = banner;
            so.FindProperty("rowRoot").objectReferenceValue = rowRt;
            so.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, PrefabName);
        }

        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[TaskAssignmentPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }
            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == "TaskAssignment")
                {
                    panels.DeleteArrayElementAtIndex(i); // refresh the entry (idempotent re-run)
                    break;
                }
            }
            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = "TaskAssignment";
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.TaskAssignment;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateChild(Transform parent, string name, params System.Type[] components)
        {
            var allComponents = new System.Type[components.Length + 1];
            allComponents[0] = typeof(RectTransform);
            components.CopyTo(allComponents, 1);
            var go = new GameObject(name, allComponents);
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
            text.color = Color.black;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font)
        {
            var go = CreateChild(parent, name, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>();
            img.color = new Color(0.8f, 0.8f, 0.8f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            var text = CreateText(go.transform, "Label", label, font);
            StretchFull(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.black;
            return button;
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
