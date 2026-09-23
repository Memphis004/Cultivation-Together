#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Phase 4 — builds the DiscipleDetail panel prefab from code + appends it to
    /// the MainPanelCatalog (idempotent). Same "UI prefab from code" convention as
    /// AvatarPrefabGenerator (same project already solves it this way), same
    /// CreateText/CreateButton/EnsureFolder helpers, same SerializedObject ref
    /// wiring for AvatarRenderer's private fields.
    /// </summary>
    public static class DiscipleDetailPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";

        [MenuItem("Xianxia/Generate DiscipleDetail Panel")]
        public static void Generate()
        {
            EnsureFolder(RootFolder);

            var layerImagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RootFolder + "/../Avatar/LayerImagePrefab.prefab");
            if (layerImagePrefab == null)
            {
                // Fallback: AvatarPrefabGenerator's own folder layout
                layerImagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Avatar/LayerImagePrefab.prefab");
            }
            if (layerImagePrefab == null)
            {
                EditorUtility.DisplayDialog("DiscipleDetailPanelGenerator",
                    "LayerImagePrefab.prefab not found — run 'Xianxia/Generate Avatar Prefabs' first (it builds the layer prefab the portrait needs).", "OK");
                return;
            }

            var prefab = BuildPanel(layerImagePrefab);
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[DiscipleDetailPanelGenerator] DiscipleDetailPanel prefab + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel(GameObject layerImagePrefab)
        {
            // ── root ── (same rules as AvatarCustomization: no ContentSizeFitter at root)
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
            windowRt.sizeDelta = new Vector2(520f, 480f);
            windowRt.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.93f, 0.88f, 0.78f, 1f);

            // Header
            var title = CreateText(window.transform, "TitleText", "Disciple");
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(-40f, 50f);
            titleRt.anchoredPosition = new Vector2(0f, -12f);
            title.fontSize = 26; title.fontStyle = FontStyle.Bold;

            // Portrait (left) — AvatarRenderer wired exactly like AvatarCustomization
            var avatarRoot = CreateChild(window.transform, "AvatarRoot", typeof(CanvasGroup));
            var avatarRt = avatarRoot.GetComponent<RectTransform>();
            avatarRt.anchorMin = new Vector2(0f, 0f); avatarRt.anchorMax = new Vector2(0f, 1f);
            avatarRt.pivot = new Vector2(0f, 0.5f);
            avatarRt.sizeDelta = new Vector2(200f, -110f);
            avatarRt.anchoredPosition = new Vector2(20f, 0f);
            avatarRoot.GetComponent<CanvasGroup>().alpha = 1f; // ⚠️ must be 1 — AvatarRenderer won't Update otherwise

            var avatarRenderer = avatarRoot.AddComponent<AvatarRenderer>();
            var rendererSo = new SerializedObject(avatarRenderer);
            var layerRootProp = rendererSo.FindProperty("layerRoot");
            var layerPrefabProp = rendererSo.FindProperty("layerPrefab");
            if (layerRootProp != null) layerRootProp.objectReferenceValue = avatarRoot.GetComponent<RectTransform>();
            if (layerPrefabProp != null) layerPrefabProp.objectReferenceValue = layerImagePrefab.GetComponent<Image>();
            rendererSo.ApplyModifiedPropertiesWithoutUndo();

            // Info column (right)
            var info = CreateChild(window.transform, "Info", typeof(VerticalLayoutGroup));
            var infoRt = info.GetComponent<RectTransform>();
            infoRt.anchorMin = new Vector2(0f, 0f); infoRt.anchorMax = new Vector2(1f, 1f);
            infoRt.pivot = new Vector2(0.5f, 0.5f);
            infoRt.offsetMin = new Vector2(240f, 80f);
            infoRt.offsetMax = new Vector2(-24f, -70f);
            var infoVLG = info.GetComponent<VerticalLayoutGroup>();
            infoVLG.spacing = 10f; infoVLG.childForceExpandWidth = true; infoVLG.childForceExpandHeight = false;

            var nameText = CreateText(info.transform, "NameText", "—");
            var rankText = CreateText(info.transform, "RankText", "—");
            var taskText = CreateText(info.transform, "TaskText", "—");
            var walletText = CreateText(info.transform, "WalletText", "—");
            foreach (var t in new[] { nameText, rankText, taskText, walletText })
            {
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 30f;
            }

            // Close button
            var closeButton = CreateButton(window.transform, "CloseButton", "Close");
            var closeRt = closeButton.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 0f); closeRt.anchorMax = new Vector2(1f, 0f);
            closeRt.pivot = new Vector2(1f, 0f);
            closeRt.sizeDelta = new Vector2(120f, 40f);
            closeRt.anchoredPosition = new Vector2(-20f, 16f);

            // View component + serialized refs
            var view = root.AddComponent<DiscipleDetailView>();
            var so = new SerializedObject(view);
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("rankText").objectReferenceValue = rankText;
            so.FindProperty("taskText").objectReferenceValue = taskText;
            so.FindProperty("walletText").objectReferenceValue = walletText;
            so.FindProperty("closeButton").objectReferenceValue = closeButton;
            so.FindProperty("portraitRenderer").objectReferenceValue = avatarRenderer;
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

        private static Text CreateText(Transform parent, string name, string content)
        {
            var go = CreateChild(parent, name, typeof(Text));
            var text = go.GetComponent<Text>();
            text.text = content;
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
