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
    /// Phase 1 — builds the DiscipleDetail panel prefab from code + appends it to
    /// the MainPanelCatalog (idempotent), same "UI prefab from code" convention as
    /// DiscipleListPanelGenerator / AvatarPrefabGenerator.
    ///
    /// โครง 3 โซนตาม reference: LeftRail (scroll วงกลม portrait สลับศิษย์) /
    /// Header (ชื่อ + rank tag) / RightTabRail (6 ปุ่ม + badge slot) + ContentHost
    /// (child ตายตัว 6 อันเรียงตาม DiscipleTab enum — view self-wire จากลำดับ)
    ///
    /// แก้จากเวอร์ชันเดิม: legacy UnityEngine.UI.Text → TextMeshProUGUI
    /// (legacy Text ไม่มี fallback ไทย) + ฟอนต์ THSarabunPSK เหมือน DiscipleList
    /// catalog entry/kind เดิม (DiscipleDetail, kind=5) — idempotent re-run
    /// </summary>
    public static class DiscipleDetailPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        private static readonly string[] TabLabels =
        {
            "ข้อมูล", "สเตตัส", "อุปกรณ์", "สกิล", "ลิขิตฟ้า", "รากปราณ",
        };

        private static readonly string[] StatusAxisLabels =
        {
            "รากฐาน", "รากกระดูก", "ปัญญา", "ศักยภาพ", "เสน่ห์", "วาสนา",
        };

        [MenuItem("Xianxia/Generate DiscipleDetail Panel")]
        public static void Generate()
        {
            EnsureFolder(RootFolder);

            var layerImagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RootFolder + "/../Avatar/LayerImagePrefab.prefab");
            if (layerImagePrefab == null)
            {
                layerImagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Avatar/LayerImagePrefab.prefab");
            }
            if (layerImagePrefab == null)
            {
                EditorUtility.DisplayDialog("DiscipleDetailPanelGenerator",
                    "LayerImagePrefab.prefab not found — run 'Xianxia/Generate Avatar Prefabs' first.", "OK");
                return;
            }

            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[DiscipleDetailPanelGenerator] Thai font not found at " + ThaiFontPath);

            var prefab = BuildPanel(layerImagePrefab, thaiFont);
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[DiscipleDetailPanelGenerator] DiscipleDetailPanel prefab (3-zone shell) + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel(GameObject layerImagePrefab, TMP_FontAsset font)
        {
            // ── root ── (no ContentSizeFitter at root — AvatarCustomization lesson)
            var root = new GameObject("DiscipleDetailPanel", typeof(RectTransform), typeof(CanvasGroup));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);

            var backdrop = CreateChild(root.transform, "Backdrop", typeof(Image));
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var window = CreateChild(root.transform, "Window", typeof(Image));
            var windowRt = window.GetComponent<RectTransform>();
            windowRt.anchorMin = windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            windowRt.pivot = new Vector2(0.5f, 0.5f);
            windowRt.sizeDelta = new Vector2(1400f, 780f); // ใหญ่ตาม reference (เดิม 520×480)
            windowRt.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.93f, 0.88f, 0.78f, 1f);

            // ── LeftRail: scroll ของวงกลม portrait (สลับศิษย์โดยไม่ปิด panel) ──
            var railScroll = CreateChild(window.transform, "LeftRail",
                typeof(Image), typeof(Mask), typeof(ScrollRect));
            var railRt = railScroll.GetComponent<RectTransform>();
            railRt.anchorMin = new Vector2(0f, 0f); railRt.anchorMax = new Vector2(0f, 1f);
            railRt.pivot = new Vector2(0f, 0.5f);
            railRt.sizeDelta = new Vector2(110f, -32f);
            railRt.anchoredPosition = new Vector2(12f, 0f);
            railScroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            railScroll.GetComponent<Mask>().showMaskGraphic = false;
            var railRect = railScroll.GetComponent<ScrollRect>();
            railRect.horizontal = false;
            railRect.vertical = true;
            railRect.movementType = ScrollRect.MovementType.Clamped;

            var railViewport = CreateChild(railScroll.transform, "Viewport", typeof(Image), typeof(Mask));
            StretchFull(railViewport.GetComponent<RectTransform>());
            railViewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            railViewport.GetComponent<Mask>().showMaskGraphic = false;
            railRect.viewport = railViewport.GetComponent<RectTransform>();

            var railContent = CreateChild(railViewport.transform, "Content",
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var railContentRt = railContent.GetComponent<RectTransform>();
            railContentRt.anchorMin = new Vector2(0f, 1f);
            railContentRt.anchorMax = new Vector2(1f, 1f);
            railContentRt.pivot = new Vector2(0.5f, 1f);
            railContentRt.anchoredPosition = Vector2.zero;
            railContentRt.sizeDelta = new Vector2(0f, 0f);
            var railVlg = railContent.GetComponent<VerticalLayoutGroup>();
            railVlg.childAlignment = TextAnchor.UpperCenter;
            railVlg.spacing = 10f;
            railVlg.padding = new RectOffset(8, 8, 10, 10);
            railVlg.childControlWidth = false;
            railVlg.childControlHeight = false;
            railVlg.childForceExpandWidth = false;
            railVlg.childForceExpandHeight = false;
            var railFitter = railContent.GetComponent<ContentSizeFitter>();
            railFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            railFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            railRect.content = railContentRt;

            // ── Header: ชื่อ + rank tag (ไม่มี class/rarity จริง — ดู ground truth) ──
            var header = CreateChild(window.transform, "Header", typeof(Image));
            var headerRt = header.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f); headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.sizeDelta = new Vector2(-300f, 70f);
            headerRt.anchoredPosition = new Vector2(60f, -8f);
            header.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);

            var headerName = CreateText(header.transform, "HeaderNameText", "—", font, 40);
            var headerNameRt = headerName.rectTransform;
            headerNameRt.anchorMin = new Vector2(0f, 0.5f); headerNameRt.anchorMax = new Vector2(0f, 0.5f);
            headerNameRt.pivot = new Vector2(0f, 0.5f);
            headerNameRt.sizeDelta = new Vector2(420f, 54f);
            headerNameRt.anchoredPosition = new Vector2(18f, 0f);

            var headerRank = CreateText(header.transform, "HeaderRankText", "—", font, 26);
            var headerRankRt = headerRank.rectTransform;
            headerRankRt.anchorMin = new Vector2(1f, 0.5f); headerRankRt.anchorMax = new Vector2(1f, 0.5f);
            headerRankRt.pivot = new Vector2(1f, 0.5f);
            headerRankRt.sizeDelta = new Vector2(260f, 40f);
            headerRankRt.anchoredPosition = new Vector2(-18f, 0f);
            headerRank.color = new Color(0.35f, 0.3f, 0.2f);
            headerRank.alignment = TextAlignmentOptions.Right;
            // TODO(backlog): class tag + rarity เมื่อมี job/class field จริง (ground truth: ไม่มี)

            // ── RightTabRail: ปุ่มแนวตั้ง 6 อัน + badge slot (ลำดับ = DiscipleTab enum) ──
            var tabRail = CreateChild(window.transform, "RightTabRail",
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var tabRailRt = tabRail.GetComponent<RectTransform>();
            tabRailRt.anchorMin = new Vector2(1f, 0f); tabRailRt.anchorMax = new Vector2(1f, 1f);
            tabRailRt.pivot = new Vector2(1f, 0.5f);
            tabRailRt.sizeDelta = new Vector2(150f, -110f);
            tabRailRt.anchoredPosition = new Vector2(-8f, 0f);
            var tabVlg = tabRail.GetComponent<VerticalLayoutGroup>();
            tabVlg.spacing = 6f;
            tabVlg.childAlignment = TextAnchor.UpperCenter;
            tabVlg.childControlWidth = true;
            tabVlg.childControlHeight = false;
            tabVlg.childForceExpandWidth = true;
            tabVlg.childForceExpandHeight = false;

            for (int i = 0; i < TabLabels.Length; i++)
            {
                var tabGo = CreateChild(tabRail.transform, "Tab_" + TabLabels[i], typeof(Image), typeof(Button), typeof(LayoutElement));
                tabGo.GetComponent<LayoutElement>().preferredHeight = 52f;
                tabGo.GetComponent<Image>().color = Color.white;

                var label = CreateText(tabGo.transform, "Label", TabLabels[i], font, 24);
                StretchFull(label.rectTransform);
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.black;
                label.raycastTarget = false;

                // badge slot (จุดแดง) — Phase 1 ปิดตลอด (presenter ส่ง false เสมอ)
                var badge = CreateChild(tabGo.transform, "Badge", typeof(Image));
                var badgeRt = badge.GetComponent<RectTransform>();
                badgeRt.anchorMin = new Vector2(1f, 1f); badgeRt.anchorMax = new Vector2(1f, 1f);
                badgeRt.pivot = new Vector2(0.5f, 0.5f);
                badgeRt.sizeDelta = new Vector2(14f, 14f);
                badgeRt.anchoredPosition = new Vector2(-8f, -8f);
                badge.GetComponent<Image>().color = new Color(0.85f, 0.15f, 0.15f);
                badge.GetComponent<Image>().raycastTarget = false;
                badge.SetActive(false);
            }

            // ── ContentHost: child ตายตัว 6 อัน (1 ต่อแท็บ, active ทีละอัน) ──
            var contentHost = CreateChild(window.transform, "ContentHost");
            var hostRt = contentHost.GetComponent<RectTransform>();
            hostRt.anchorMin = new Vector2(0f, 0f); hostRt.anchorMax = new Vector2(1f, 1f);
            hostRt.pivot = new Vector2(0.5f, 0.5f);
            hostRt.offsetMin = new Vector2(140f, 24f);
            hostRt.offsetMax = new Vector2(-170f, -86f);

            // 0) Info — text 4 บรรทัด (field เดิมของ view) + placeholder สายบำเพ็ญ
            var infoContent = CreateChild(contentHost.transform, "InfoContent",
                typeof(VerticalLayoutGroup));
            StretchFull(infoContent.GetComponent<RectTransform>());
            var infoVlg = infoContent.GetComponent<VerticalLayoutGroup>();
            infoVlg.spacing = 10f;
            infoVlg.childAlignment = TextAnchor.UpperLeft;
            infoVlg.childControlWidth = true;
            infoVlg.childControlHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.childForceExpandHeight = false;

            var nameText = CreateText(infoContent.transform, "NameText", "—", font, 34);
            var rankText = CreateText(infoContent.transform, "RankText", "—", font, 26);
            var taskText = CreateText(infoContent.transform, "TaskText", "—", font, 26);
            var walletText = CreateText(infoContent.transform, "WalletText", "—", font, 26);
            var cultivationNote = CreateText(infoContent.transform, "CultivationNoteText",
                "ระบบสายบำเพ็ญ — ยังไม่มีข้อมูล (placeholder)", font, 24);
            cultivationNote.color = new Color(0.45f, 0.42f, 0.35f);
            foreach (var t in new[] { nameText, rankText, taskText, walletText, cultivationNote })
            {
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 44f;
            }

            // 1) Status — radar chart + 6 label (mock data ผูกใน presenter เท่านั้น)
            var statusContent = CreateChild(contentHost.transform, "StatusContent");
            StretchFull(statusContent.GetComponent<RectTransform>());
            var radarGo = new GameObject("RadarChart", typeof(RectTransform), typeof(RadarChartGraphic));
            var radarRt = (RectTransform)radarGo.transform;
            radarRt.SetParent(statusContent.transform, false);
            radarRt.anchorMin = new Vector2(0f, 0.5f); radarRt.anchorMax = new Vector2(0f, 0.5f);
            radarRt.pivot = new Vector2(0f, 0.5f);
            radarRt.sizeDelta = new Vector2(380f, 380f);
            radarRt.anchoredPosition = new Vector2(60f, 0f);
            var radar = radarGo.GetComponent<RadarChartGraphic>();

            for (int a = 0; a < StatusAxisLabels.Length; a++)
            {
                float angle = Mathf.PI / 2f - 2f * Mathf.PI * a / StatusAxisLabels.Length;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var labelGo = new GameObject("AxisLabel_" + StatusAxisLabels[a], typeof(RectTransform));
                var labelRt = (RectTransform)labelGo.transform;
                labelRt.SetParent(statusContent.transform, false);
                labelRt.anchorMin = new Vector2(0.5f, 0.5f); labelRt.anchorMax = new Vector2(0.5f, 0.5f);
                labelRt.pivot = dir.x < -0.01f ? new Vector2(1f, 0.5f) : (dir.x > 0.01f ? new Vector2(0f, 0.5f) : new Vector2(0.5f, dir.y > 0 ? 0f : 1f));
                labelRt.sizeDelta = new Vector2(120f, 30f);
                labelRt.anchoredPosition = radarRt.anchoredPosition + dir * 215f;

                var label = labelGo.AddComponent<TextMeshProUGUI>();
                if (font != null) label.font = font;
                label.text = StatusAxisLabels[a];
                label.fontSize = 22;
                label.color = new Color(0.25f, 0.22f, 0.16f);
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }

            var mockNote = CreateText(statusContent.transform, "MockNoteText",
                "สเตตัสเป็น placeholder (mock ต่อคนแบบ deterministic) — รอ DiscipleAttributes จริง", font, 20);
            var mockNoteRt = mockNote.rectTransform;
            mockNoteRt.anchorMin = new Vector2(0.5f, 0f); mockNoteRt.anchorMax = new Vector2(0.5f, 0f);
            mockNoteRt.pivot = new Vector2(0.5f, 0f);
            mockNoteRt.sizeDelta = new Vector2(-40f, 30f);
            mockNoteRt.anchoredPosition = new Vector2(0f, 6f);
            mockNote.color = new Color(0.45f, 0.42f, 0.35f);
            mockNote.alignment = TextAlignmentOptions.Center;

            // 2-5) Equipment / Skill / Destiny / SpiritRoot — placeholder เรียบ ๆ
            // (ไม่แต่งเงื่อนไขปลดล็อกที่ไม่มีจริง — ดู ground truth ในแผน)
            foreach (var tabName in new[] { "EquipmentContent", "SkillContent", "DestinyContent", "SpiritRootContent" })
            {
                var ph = CreateChild(contentHost.transform, tabName, typeof(VerticalLayoutGroup));
                StretchFull(ph.GetComponent<RectTransform>());
                var vlg = ph.GetComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;

                var note = CreateText(ph.transform, "PlaceholderText", "ยังไม่พร้อมใช้งาน", font, 30);
                var le = note.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 44f;
                note.color = new Color(0.45f, 0.42f, 0.35f);
            }

            // ── Portrait กลาง (FullBody) — ตรงกลางระหว่าง rail/tabs ──
            var portraitArea = CreateChild(window.transform, "PortraitArea", typeof(CanvasGroup));
            var portraitRt = portraitArea.GetComponent<RectTransform>();
            portraitRt.anchorMin = new Vector2(0f, 0f); portraitRt.anchorMax = new Vector2(0f, 1f);
            portraitRt.pivot = new Vector2(0f, 0.5f);
            portraitRt.sizeDelta = new Vector2(430f, -140f);
            portraitRt.anchoredPosition = new Vector2(140f, 0f);
            portraitArea.GetComponent<CanvasGroup>().alpha = 1f; // ⚠️ =1 — AvatarRenderer ไม่ Update ถ้าโปร่งใส

            var portraitRenderer = portraitArea.AddComponent<AvatarRenderer>();
            var rendererSo = new SerializedObject(portraitRenderer);
            var layerRootProp = rendererSo.FindProperty("layerRoot");
            var layerPrefabProp = rendererSo.FindProperty("layerPrefab");
            if (layerRootProp != null) layerRootProp.objectReferenceValue = portraitArea.GetComponent<RectTransform>();
            if (layerPrefabProp != null) layerPrefabProp.objectReferenceValue = layerImagePrefab.GetComponent<Image>();
            rendererSo.ApplyModifiedPropertiesWithoutUndo();

            // ── Close button ──
            var closeButton = CreateButton(window.transform, "CloseButton", "ปิด", font);
            var closeRt = closeButton.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f); closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(110f, 44f);
            closeRt.anchoredPosition = new Vector2(-12f, -12f);

            // ── View + serialized refs (shell refs เท่านั้น — tabs/contents view self-wire) ──
            var view = root.AddComponent<DiscipleDetailView>();
            var so = new SerializedObject(view);
            SetRef(so, "nameText", nameText);
            SetRef(so, "rankText", rankText);
            SetRef(so, "taskText", taskText);
            SetRef(so, "walletText", walletText);
            SetRef(so, "closeButton", closeButton);
            SetRef(so, "portraitRenderer", portraitRenderer);
            SetRef(so, "railContent", railContentRt);
            SetRef(so, "tabRail", tabRail.GetComponent<RectTransform>());
            SetRef(so, "contentHost", hostRt);
            SetRef(so, "headerNameText", headerName);
            SetRef(so, "headerRankText", headerRank);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "DiscipleDetailPanel");
        }

        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[DiscipleDetailPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }
            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == "DiscipleDetail")
                {
                    panels.DeleteArrayElementAtIndex(i); // refresh the entry (idempotent re-run)
                    break;
                }
            }
            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = "DiscipleDetail";
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.DiscipleDetail;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(SerializedObject so, string fieldName, Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning("[DiscipleDetailPanelGenerator] missing field '" + fieldName + "'");
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

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, TMP_FontAsset font, float size)
        {
            var go = CreateChild(parent, name, typeof(TextMeshProUGUI));
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null) text.font = font;
            text.fontSize = size;
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
            var text = CreateText(go.transform, "Label", label, font, 24);
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
