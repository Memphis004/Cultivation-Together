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
    /// DiscipleList panel — builds the prefab from code + appends it to the
    /// MainPanelCatalog (idempotent). Same "UI prefab from code" convention as
    /// DiscipleDetailPanelGenerator / AvatarPrefabGenerator.
    ///
    /// เหมือนกับ ResourcePopupPrefab: โครงหลัก (title / close / scroll) ถูก
    /// serialize ครบหลัง import แต่ "ใบการ์ด" ไม่อยู่ใน prefab เลย —
    /// DiscipleListView.CreateCard สร้างที่ runtime (แพทเทิร์นเดียวกับ
    /// ResourcePopupView.CreateRow ที่เคยเจอ template หายตอน import)
    /// โครงภายในการ์ด (HorizontalLayoutGroup คุม icon/text/wallet + LayoutElement
    /// ต่อชิ้น) ก็ถูกสร้างใน CreateCard เช่นกัน — แก้ layout ที่นั่น ไม่ใช่ใน prefab
    ///
    /// titleText ใช้ THSarabunPSK SDF (Thai-capable) — การ์ด inherit ฟอนต์นี้ผ่าน
    /// titleText.font เหมือน ResourcePopupView.CreateText (LiberationSans ไม่มีไทย)
    /// </summary>
    public static class DiscipleListPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        [MenuItem("Xianxia/Generate DiscipleList Panel")]
        public static void Generate()
        {
            EnsureFolder(RootFolder);

            var prefab = BuildPanel();
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[DiscipleListPanelGenerator] DiscipleListPanel prefab + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel()
        {
            // ── root ── (no ContentSizeFitter at root — AvatarCustomization lesson)
            var root = new GameObject("DiscipleListPanel", typeof(RectTransform), typeof(CanvasGroup));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);

            var backdrop = CreateChild(root.transform, "Backdrop", typeof(Image));
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var window = CreateChild(root.transform, "Window", typeof(Image));
            var windowRt = window.GetComponent<RectTransform>();
            windowRt.anchorMin = windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            windowRt.pivot = new Vector2(0.5f, 0.5f);
            windowRt.sizeDelta = new Vector2(460f, 560f);
            windowRt.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.93f, 0.88f, 0.78f, 1f);

            // Thai font — การ์ด inherit ผ่าน titleText.font (ResourcePopupView pattern)
            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[DiscipleListPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — cards would fall back to the default font (no Thai glyphs)");

            // Title
            var title = CreateText(window.transform, "TitleText", "ศิษย์สำนัก", thaiFont);
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

            // Scroll view (fills the rest of the window)
            var scrollView = CreateChild(window.transform, "ScrollView",
                typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
            var scrollLe = scrollView.GetComponent<LayoutElement>();
            scrollLe.ignoreLayout = true;
            var scrollRt = scrollView.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f); scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.pivot = new Vector2(0.5f, 0.5f);
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -60f);
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

            // Content = cardsRoot — VerticalLayoutGroup จะถูกเพิ่มซ้ำโดย
            // EnsureCardsLayout ถ้าหายตอน import (defensive, เหมือน ResourcePopup)
            var content = CreateChild(viewport.transform, "Content",
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // ← ตัวเดียวที่อนุญาตใน prefab

            scrollRect.content = contentRt;

            // View component + serialized refs (title/close/scroll only —
            // cards are runtime-built by CreateCard, ResourcePopupView pattern)
            var view = root.AddComponent<DiscipleListView>();
            var so = new SerializedObject(view);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("cardsRoot").objectReferenceValue = contentRt;
            so.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "DiscipleListPanel");
        }

        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[DiscipleListPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }
            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == "DiscipleList")
                {
                    panels.DeleteArrayElementAtIndex(i); // refresh the entry (idempotent re-run)
                    break;
                }
            }
            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = "DiscipleList";
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.DiscipleList;
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
