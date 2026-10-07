#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;
namespace Xianxia.Sect.EditorTools
{
    public static class DiscipleListPanelGenerator
    {
        [MenuItem("Xianxia/Generate DiscipleList Panel")]
        public static void Generate()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/THSarabunPSK SDF.asset");
            var root = InkWidgets.Rect(null, "DiscipleListPanel", 0, 0, 1920, 1080);
            InkWidgets.Stretch(root); root.gameObject.AddComponent<CanvasGroup>(); root.gameObject.AddComponent<DiscipleModalScope>();
            var backdrop = InkWidgets.Rect(root, "Backdrop", 0, 0, 1920, 1080); InkWidgets.Stretch(backdrop); InkWidgets.Fill(backdrop, UiPalette.Backdrop, true);
            var window = InkWidgets.InkPanel(root, "Window", 0, 0, 1400, 790);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f,.5f); window.anchoredPosition = Vector2.zero;
            var plate = InkWidgets.InkPlate(window, "TitlePlate", "ศิษย์สำนัก", font, 24, 20, 420, 56);
            var close = InkWidgets.CloseButton(window, font, 1328, 20);
            var search = InkWidgets.InkPanel(window, "Search", 780, 24, 320, 48);
            var searchImage = search.Find("Paper").GetComponent<Image>(); searchImage.raycastTarget = true;
            var input = search.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = searchImage;
            var searchText = InkWidgets.Text(search, "Text", "", font, 28, 12, 0, 296, 48);
            var placeholder = InkWidgets.Text(search, "Placeholder", "ค้นหาศิษย์", font, 28, 12, 0, 296, 48, UiPalette.Secondary);
            input.textViewport = search; input.textComponent = searchText as TextMeshProUGUI; input.placeholder = placeholder;
            var sort = InkWidgets.OutlineButton(window, "SortButton", "เรียงชื่อ ↑↓", font, 1120, 24, 184, 48);
            var viewport = InkWidgets.Rect(window, "Viewport", 70, 96, 1260, 610);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = InkWidgets.Rect(viewport, "Content", 0, 0, 1260, 316);
            content.anchorMin = new Vector2(0,1); content.anchorMax = new Vector2(1,1); content.sizeDelta = new Vector2(0,316);
            var grid = content.gameObject.AddComponent<FitWidthGridLayoutGroup>(); grid.Columns = 5; grid.SpacingValue = 20; grid.CellAspect = 316f/236; grid.RowsInView = 1; grid.childAlignment = TextAnchor.UpperLeft;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content; scroll.viewport = viewport;
            var footer = InkWidgets.Text(window, "Footer", "", font, 28, 28, 724, 1344, 46, UiPalette.Secondary);
            var view = root.gameObject.AddComponent<DiscipleListView>();
            var so = new SerializedObject(view);
            Set(so,"titleText",plate.Find("Label").GetComponent<TMP_Text>()); Set(so,"closeButton",close); Set(so,"cardsRoot",content); Set(so,"scrollRect",scroll);
            Set(so,"footerText",footer); Set(so,"searchInput",input); Set(so,"sortButton",sort); so.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/Prefabs/UI/DiscipleListPanel.prefab"); Object.DestroyImmediate(root.gameObject);
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>("Assets/Panel Catalog/MainPanelCatalog.asset");
            var cs = new SerializedObject(catalog); var panels = cs.FindProperty("panels");
            bool found = false;
            for(int i=0;i<panels.arraySize;i++) { var el=panels.GetArrayElementAtIndex(i); if(el.FindPropertyRelative("PanelId").stringValue!="DiscipleList")continue; el.FindPropertyRelative("Prefab").objectReferenceValue=prefab;found=true; }
            if(!found) { panels.arraySize++;var el=panels.GetArrayElementAtIndex(panels.arraySize-1);el.FindPropertyRelative("PanelId").stringValue="DiscipleList";el.FindPropertyRelative("Prefab").objectReferenceValue=prefab;el.FindPropertyRelative("PresenterKind").enumValueIndex=(int)UIPresenterKind.DiscipleList; }
            cs.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
        }
        private static void Set(SerializedObject so,string name,Object value) => so.FindProperty(name).objectReferenceValue=value;
    }
}
#endif
