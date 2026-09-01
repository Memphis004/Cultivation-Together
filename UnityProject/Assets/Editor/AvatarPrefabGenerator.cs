#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.UI;

namespace Xianxia.EditorTools
{
    /// <summary>
    /// สร้าง Prefab ชุด Avatar Customization ทั้งหมดอัตโนมัติ
    /// รัน: เมนู Xianxia > Generate Avatar Prefabs
    /// อ้างอิงโครงจาก AvatarCustomizationView / AvatarOptionButton ที่เขียนไว้แล้ว
    /// </summary>
    public static class AvatarPrefabGenerator
    {
        private const string RootFolder = "Assets/Prefabs/Avatar";

        [MenuItem("Xianxia/Generate Avatar Prefabs")]
        public static void GenerateAll()
        {
            EnsureFolder(RootFolder);

            // ลำดับสำคัญ: ต้องสร้าง 3 ตัวนี้ก่อน เพราะ Panel ต้องลาก reference ไปใส่
            GameObject layerImagePrefab   = GenerateLayerImagePrefab();
            GameObject slotTabPrefab      = GenerateSlotTabButtonPrefab();
            GameObject optionButtonPrefab = GenerateAvatarOptionButtonPrefab();

            GenerateAvatarCustomizationPanel(layerImagePrefab, slotTabPrefab, optionButtonPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AvatarPrefabGenerator] สร้าง Prefab ครบ 4 ไฟล์ที่ " + RootFolder);
        }

        // ══════════════════════════════════════════════════════
        // 1) LayerImagePrefab — layer เปล่าให้ AvatarRenderer instantiate
        // ══════════════════════════════════════════════════════
        private static GameObject GenerateLayerImagePrefab()
        {
            var root = new GameObject("LayerImagePrefab", typeof(RectTransform));
            var rt = root.GetComponent<RectTransform>();
            StretchFull(rt);

            var img = root.AddComponent<Image>();
            img.sprite = null;
            img.preserveAspect = true;
            img.raycastTarget = false;   // layer ไม่ควรบังคลิก preview

            return SavePrefab(root, "LayerImagePrefab");
        }

        // ══════════════════════════════════════════════════════
        // 2) SlotTabButtonPrefab — ปุ่มแท็บ slot (body/head/hair/accessory)
        // ══════════════════════════════════════════════════════
        private static GameObject GenerateSlotTabButtonPrefab()
        {
            var root = new GameObject("SlotTabButtonPrefab",
                typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 40f);

            var img = root.GetComponent<Image>();
            img.color = Color.white;

            var button = root.GetComponent<Button>();
            button.targetGraphic = img;

            var label = CreateText(root.transform, "Label", "");
            StretchFull(label.rectTransform);
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 16;

            return SavePrefab(root, "SlotTabButtonPrefab");
        }

        // ══════════════════════════════════════════════════════
        // 3) AvatarOptionButtonPrefab — ปุ่มลูกในกริดตัวเลือก
        // ══════════════════════════════════════════════════════
        private static GameObject GenerateAvatarOptionButtonPrefab()
        {
            var root = new GameObject("AvatarOptionButtonPrefab",
                typeof(RectTransform), typeof(Button));
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(96f, 116f);

            // Thumbnail (top, สูง 80)
            var thumbGO = CreateChild(root.transform, "Thumbnail", typeof(Image));
            var thumbRt = thumbGO.GetComponent<RectTransform>();
            SetAnchor(thumbRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            thumbRt.sizeDelta = new Vector2(0f, 80f);
            thumbRt.anchoredPosition = Vector2.zero;
            var thumbImg = thumbGO.GetComponent<Image>();
            thumbImg.preserveAspect = true;

            // Label (bottom, สูง 28)
            var label = CreateText(root.transform, "Label", "");
            var labelRt = label.rectTransform;
            SetAnchor(labelRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            labelRt.sizeDelta = new Vector2(0f, 28f);
            labelRt.anchoredPosition = Vector2.zero;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 14;

            // SelectedFrame (เต็มพื้นที่, เริ่มปิด)
            var frameGO = CreateChild(root.transform, "SelectedFrame", typeof(Image));
            StretchFull(frameGO.GetComponent<RectTransform>());
            var frameImg = frameGO.GetComponent<Image>();
            frameImg.color = new Color(1f, 0.85f, 0.2f, 1f);
            frameImg.raycastTarget = false;
            frameGO.SetActive(false);

            // LockedOverlay (เต็มพื้นที่, เริ่มปิด)
            var lockGO = CreateChild(root.transform, "LockedOverlay", typeof(Image));
            StretchFull(lockGO.GetComponent<RectTransform>());
            var lockImg = lockGO.GetComponent<Image>();
            lockImg.color = new Color(0f, 0f, 0f, 0.7f);
            lockImg.raycastTarget = false;
            lockGO.SetActive(false);

            var button = root.GetComponent<Button>();
            button.targetGraphic = thumbImg;

            // ── bind field ของ AvatarOptionButton ผ่าน SerializedObject ──
            var optionButton = root.AddComponent<AvatarOptionButton>();
            var so = new SerializedObject(optionButton);
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("thumbnail").objectReferenceValue = thumbImg;
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("selectedFrame").objectReferenceValue = frameGO;
            so.FindProperty("lockedOverlay").objectReferenceValue = lockGO;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "AvatarOptionButtonPrefab");
        }

        // ══════════════════════════════════════════════════════
        // 4) AvatarCustomizationPanel — ตัวหลัก
        // ══════════════════════════════════════════════════════
        private static void GenerateAvatarCustomizationPanel(
            GameObject layerImagePrefab, GameObject slotTabPrefab, GameObject optionButtonPrefab)
        {
            // ── root ──
            var root = new GameObject("AvatarCustomizationPanel",
                typeof(RectTransform), typeof(CanvasGroup));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);
            // ⚠️ ห้ามใส่ ContentSizeFitter ที่ root — คือสาเหตุบั๊กเดิม

            // ── Backdrop ──
            var backdrop = CreateChild(root.transform, "Backdrop", typeof(Image));
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            // ── Window ──
            var window = CreateChild(root.transform, "Window", typeof(Image));
            var windowRt = window.GetComponent<RectTransform>();
            windowRt.anchorMin = windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            windowRt.pivot = new Vector2(0.5f, 0.5f);
            windowRt.sizeDelta = new Vector2(900f, 620f);
            windowRt.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.93f, 0.88f, 0.78f, 1f);

            // ── Header ──
            var header = CreateChild(window.transform, "Header",
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var headerLE = header.GetComponent<LayoutElement>();
            headerLE.preferredHeight = 70f;
            var headerHLG = header.GetComponent<HorizontalLayoutGroup>();
            headerHLG.padding = new RectOffset(20, 20, 10, 10);
            headerHLG.spacing = 8f;

            var titleText = CreateText(header.transform, "TitleText", "ปรับแต่งรูปลักษณ์");
            titleText.fontSize = 28;
            titleText.fontStyle = FontStyle.Bold;

            var subtitleText = CreateText(header.transform, "SubtitleText", "");
            subtitleText.fontSize = 18;
            subtitleText.color = new Color(0.4f, 0.4f, 0.4f);

            // ── Body ──
            var body = CreateChild(window.transform, "Body",
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var bodyLE = body.GetComponent<LayoutElement>();
            bodyLE.flexibleHeight = 1f;
            var bodyHLG = body.GetComponent<HorizontalLayoutGroup>();
            bodyHLG.padding = new RectOffset(20, 20, 10, 10);
            bodyHLG.spacing = 16f;
            bodyHLG.childForceExpandWidth = true;
            bodyHLG.childForceExpandHeight = true;

            // -- PreviewPane --
            var previewPane = CreateChild(body.transform, "PreviewPane", typeof(LayoutElement));
            var previewLE = previewPane.GetComponent<LayoutElement>();
            previewLE.preferredWidth = 320f;
            previewLE.flexibleWidth = 0f;

            var avatarRoot = CreateChild(previewPane.transform, "AvatarRoot",
                typeof(CanvasGroup));
            StretchFull(avatarRoot.GetComponent<RectTransform>());
            var canvasGroup = avatarRoot.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;   // ⚠️ ต้อง = 1 ไม่งั้น AvatarRenderer ไม่ Update()

            var avatarRenderer = avatarRoot.AddComponent<AvatarRenderer>();
            var rendererSo = new SerializedObject(avatarRenderer);
            var layerRootProp = rendererSo.FindProperty("layerRoot");
            var layerPrefabProp = rendererSo.FindProperty("layerPrefab");
            if (layerRootProp != null) layerRootProp.objectReferenceValue = avatarRoot.GetComponent<RectTransform>();
            if (layerPrefabProp != null) layerPrefabProp.objectReferenceValue = layerImagePrefab.GetComponent<Image>();
            rendererSo.ApplyModifiedPropertiesWithoutUndo();

            // -- EditPane --
            var editPane = CreateChild(body.transform, "EditPane",
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var editLE = editPane.GetComponent<LayoutElement>();
            editLE.flexibleWidth = 1f;
            var editVLG = editPane.GetComponent<VerticalLayoutGroup>();
            editVLG.spacing = 12f;
            editVLG.childForceExpandWidth = true;

            // SlotTabBar
            var slotTabBar = CreateChild(editPane.transform, "SlotTabBar",
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var tabLE = slotTabBar.GetComponent<LayoutElement>();
            tabLE.preferredHeight = 44f;
            var tabHLG = slotTabBar.GetComponent<HorizontalLayoutGroup>();
            tabHLG.spacing = 6f;
            tabHLG.childAlignment = TextAnchor.MiddleLeft;

            // ScrollView
            var scrollView = CreateChild(editPane.transform, "ScrollView",
                typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
            var scrollLE = scrollView.GetComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollView.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            scrollView.GetComponent<Mask>().showMaskGraphic = false;
            var scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            var viewport = CreateChild(scrollView.transform, "Viewport", typeof(Image), typeof(Mask));
            StretchFull(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewport.GetComponent<RectTransform>();

            var content = CreateChild(viewport.transform, "Content",
                typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = content.GetComponent<RectTransform>();
            SetAnchor(contentRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            contentRt.anchoredPosition = Vector2.zero;

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(96f, 116f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // ← ตัวเดียวในทั้ง prefab ที่อนุญาต

            scrollRect.content = contentRt;

            // ── Footer ──
            var footer = CreateChild(window.transform, "Footer",
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var footerLE = footer.GetComponent<LayoutElement>();
            footerLE.preferredHeight = 60f;
            var footerHLG = footer.GetComponent<HorizontalLayoutGroup>();
            footerHLG.padding = new RectOffset(20, 20, 10, 10);
            footerHLG.spacing = 10f;
            footerHLG.childAlignment = TextAnchor.MiddleRight;

            var errorText = CreateText(footer.transform, "ErrorText", "");
            errorText.color = Color.red;
            var errorLE = errorText.gameObject.AddComponent<LayoutElement>();
            errorLE.flexibleWidth = 1f;
            errorText.gameObject.SetActive(false);

            var randomizeButton = CreateButton(footer.transform, "RandomizeButton", "สุ่ม");
            var cancelButton    = CreateButton(footer.transform, "CancelButton", "ยกเลิก");
            var confirmButton   = CreateButton(footer.transform, "ConfirmButton", "ยืนยัน");
            confirmButton.targetGraphic.color = new Color(0.9f, 0.75f, 0.2f); // เน้นสีทอง

            // ── bind field ของ AvatarCustomizationView ──
            var view = root.AddComponent<AvatarCustomizationView>();
            var viewSo = new SerializedObject(view);
            SetRef(viewSo, "titleText", titleText);
            SetRef(viewSo, "subtitleText", subtitleText);
            SetRef(viewSo, "previewRenderer", avatarRenderer);
            SetRef(viewSo, "slotTabRoot", slotTabBar.GetComponent<RectTransform>());
            SetRef(viewSo, "slotTabPrefab", slotTabPrefab.GetComponent<Button>());
            SetRef(viewSo, "optionGridRoot", contentRt);
            SetRef(viewSo, "optionButtonPrefab", optionButtonPrefab.GetComponent<AvatarOptionButton>());
            SetRef(viewSo, "confirmButton", confirmButton);
            SetRef(viewSo, "cancelButton", cancelButton);
            SetRef(viewSo, "randomizeButton", randomizeButton);
            SetRef(viewSo, "errorText", errorText);
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, "AvatarCustomizationPanel");
        }

        // ══════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════

        private static void SetRef(SerializedObject so, string fieldName, Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("[AvatarPrefabGenerator] ไม่พบ field ชื่อ '" + fieldName +
                                  "' ใน " + so.targetObject.GetType().Name +
                                  " — เช็กว่าชื่อ field ตรงกับสคริปต์จริงหรือไม่");
                return;
            }
            prop.objectReferenceValue = value;
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

        private static void SetAnchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
        }

        private static Text CreateText(Transform parent, string name, string content)
        {
            var go = CreateChild(parent, name, typeof(Text));
            var text = go.GetComponent<Text>();
            text.text = content;
            
            // แก้ไข: ใช้ LegacyRuntime.ttf แทน Arial.ttf สำหรับ Unity เวอร์ชันใหม่
            // หากต้องการรองรับทั้งเวอร์ชันเก่าและใหม่ สามารถใช้ try-catch หรือตรวจสอบเวอร์ชันได้
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            var go = CreateChild(parent, name, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>();
            img.color = new Color(0.8f, 0.8f, 0.8f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;

            var text = CreateText(go.transform, "Label", label);
            StretchFull(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 100f;
            le.preferredHeight = 40f;

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