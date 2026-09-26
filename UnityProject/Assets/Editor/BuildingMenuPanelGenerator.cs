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
    /// BuildingMenu panel — builds the prefab from code + appends it to the
    /// MainPanelCatalog (idempotent). Same "UI prefab from code" convention as
    /// DiscipleListPanelGenerator / DiscipleDetailPanelGenerator.
    ///
    /// โครงหลัก (title / close / tabRoot / itemRoot scroll) ถูก serialize ครบหลัง
    /// import แต่ "แท็บหมวดหมู่ + การ์ดอาคาร" ไม่อยู่ใน prefab เลย —
    /// BuildingMenuView.CreateTab/CreateItem สร้างที่ runtime (แพทเทิร์นเดียวกับ
    /// DiscipleListView.CreateCard ที่ template เคยหายตอน import)
    /// titleText ใช้ THSarabunPSK SDF (Thai-capable) — แท็บ/การ์ด inherit
    /// ฟอนต์นี้ผ่าน titleText.font เหมือน ResourcePopupView.CreateText
    /// </summary>
    public static class BuildingMenuPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        [MenuItem("Xianxia/Generate BuildingMenu Panel")]
        public static void Generate()
        {
            EnsureFolder(RootFolder);

            var prefab = BuildPanel();
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildingMenuPanelGenerator] BuildingMenuPanel prefab + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel()
        {
            // ── root ── (no ContentSizeFitter at root — AvatarCustomization lesson)
            var root = new GameObject("BuildingMenuPanel", typeof(RectTransform), typeof(CanvasGroup));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);

            var backdrop = CreateChild(root.transform, "Backdrop", typeof(Image));
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var window = CreateChild(root.transform, "Window", typeof(Image));
            var windowRt = window.GetComponent<RectTransform>();
            windowRt.anchorMin = windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            windowRt.pivot = new Vector2(0.5f, 0.5f);
            windowRt.sizeDelta = new Vector2(640f, 680f);
            windowRt.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.93f, 0.88f, 0.78f, 1f);

            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[BuildingMenuPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — tabs/cards would fall back to the default font (no Thai glyphs)");

            // Title
            var title = CreateText(window.transform, "TitleText", "สร้างสิ่งก่อสร้าง", thaiFont);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(-140f, 50f);
            titleRt.anchoredPosition = new Vector2(0f, -8f);
            title.fontSize = 34;
            title.alignment = TextAlignmentOptions.Left;

            // Close button (top-right)
            var closeButton = CreateButton(window.transform, "CloseButton", "ปิด", thaiFont);
            var closeRt = closeButton.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f); closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(110f, 40f);
            closeRt.anchoredPosition = new Vector2(-12f, -8f);

            // Category tabs (4 — cells are runtime-built by CreateTab)
            var tabRoot = CreateChild(window.transform, "TabRoot", typeof(RectTransform));
            var tabRt = tabRoot.GetComponent<RectTransform>();
            tabRt.anchorMin = new Vector2(0f, 1f); tabRt.anchorMax = new Vector2(1f, 1f);
            tabRt.pivot = new Vector2(0.5f, 1f);
            tabRt.sizeDelta = new Vector2(-16f, 64f);
            tabRt.anchoredPosition = new Vector2(0f, -62f);

            // Scroll view (fills the rest of the window) — items are runtime-built
            var scrollView = CreateChild(window.transform, "ScrollView",
                typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
            var scrollLe = scrollView.GetComponent<LayoutElement>();
            scrollLe.ignoreLayout = true;
            var scrollRt = scrollView.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f); scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.pivot = new Vector2(0.5f, 0.5f);
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -136f);
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

            // Content = itemRoot — GridLayoutGroup เพิ่มซ้ำโดย EnsureItemLayout
            // ถ้าหายตอน import (defensive, เหมือน ResourcePopup/DiscipleList)
            var content = CreateChild(viewport.transform, "Content", typeof(RectTransform));
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            scrollRect.content = contentRt;

            // View component + serialized refs (title/close/tabRoot/itemRoot/scroll only —
            // tabs + cards are runtime-built by CreateTab/CreateItem)
            var view = root.AddComponent<BuildingMenuView>();
            var so = new SerializedObject(view);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("tabRoot").objectReferenceValue = tabRt;
            so.FindProperty("itemRoot").objectReferenceValue = contentRt;
            so.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "BuildingMenuPanel");
        }

        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[BuildingMenuPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }
            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == "BuildingMenu")
                {
                    panels.DeleteArrayElementAtIndex(i); // refresh the entry (idempotent re-run)
                    break;
                }
            }
            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = "BuildingMenu";
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.BuildingMenu;
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
