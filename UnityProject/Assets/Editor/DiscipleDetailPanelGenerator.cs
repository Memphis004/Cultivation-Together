#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;
namespace Xianxia.Sect.EditorTools
{
    public static class DiscipleDetailPanelGenerator
    {
        [MenuItem("Xianxia/Generate DiscipleDetail Panel")]
        public static void Generate()
        {
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/THSarabunPSK SDF.asset");
            var root=InkWidgets.Rect(null,"DiscipleDetailPanel",0,0,1920,1080);InkWidgets.Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>();root.gameObject.AddComponent<DiscipleModalScope>();
            var backdrop=InkWidgets.Rect(root,"Backdrop",0,0,1920,1080);InkWidgets.Stretch(backdrop);InkWidgets.Fill(backdrop,UiPalette.Backdrop,true);
            var window=InkWidgets.InkPanel(root,"Window",0,0,1400,790);
            window.anchorMin=window.anchorMax=window.pivot=new Vector2(.5f,.5f);window.anchoredPosition=Vector2.zero;
            var tabs=InkWidgets.Rect(window,"TabRail",24,20,1230,52);
            string[] labels={"ข้อมูล","สเตตัส","อุปกรณ์","สกิล","ลิขิตฟ้า","รากปราณ"};
            for(int i=0;i<6;i++)InkWidgets.OutlineButton(tabs,"Tab_"+i,labels[i],font,i*202,0,190,52);
            var close=InkWidgets.CloseButton(window,font,1328,20);
            var column=InkWidgets.InkPanel(window,"PortraitColumn",24,90,408,602,UiPalette.Portrait);
            var portrait=InkWidgets.Rect(column,"PortraitArea",54,12,300,450);
            portrait.gameObject.AddComponent<CanvasGroup>();var renderer=portrait.gameObject.AddComponent<AvatarRenderer>();
            var layer=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Avatar/LayerImagePrefab.prefab");
            renderer.Configure(portrait,layer.GetComponent<Image>());
            portrait.gameObject.AddComponent<PortraitOverrideBinding>();
            InkWidgets.OutlineButton(column,"AssignTask","มอบหมายงาน",font,54,476,300,48,false);
            InkWidgets.OutlineButton(column,"Gift","ของขวัญ",font,54,538,300,48,false);
            var header=InkWidgets.InkPlate(window,"Header","",font,452,90,924,62);
            var name=header.Find("Label").GetComponent<TMP_Text>();name.rectTransform.sizeDelta=new Vector2(540,62);
            var rank=InkWidgets.Text(header,"HeaderRankText","",font,28,570,0,338,62,UiPalette.LightText,TextAlignmentOptions.MidlineRight);
            var host=InkWidgets.Rect(window,"ContentHost",452,170,924,522);
            var info=InkWidgets.Rect(host,"InfoContent",0,0,924,522);InkWidgets.Stretch(info);
            var nameValue=InkWidgets.StatRow(info,"NameRow","ชื่อ","",font,0,0,924,UiPalette.Jade);
            var rankValue=InkWidgets.StatRow(info,"RankRow","ตำแหน่ง","",font,0,62,924,UiPalette.Blue);
            var taskValue=InkWidgets.StatRow(info,"TaskRow","งานปัจจุบัน","",font,0,124,924,UiPalette.Vermilion);
            var walletValue=InkWidgets.StatRow(info,"WalletRow","ทรัพยากร","",font,0,186,924,UiPalette.Jade);
            // Wallet is also repeated in the bottom bar; allow its longer two-part value room.
            walletValue.rectTransform.anchoredPosition=new Vector2(260,-0);walletValue.rectTransform.sizeDelta=new Vector2(654,48);
            var status=InkWidgets.Rect(host,"StatusContent",0,0,924,522);InkWidgets.Stretch(status);
            string[] axes={"รากฐาน","รากกระดูก","ปัญญา","ศักยภาพ","เสน่ห์","วาสนา"};
            for(int c=0;c<3;c++)
            {
                var group=InkWidgets.InkPanel(status,"Group_"+c,c*312,52,300,240);
                InkWidgets.InkPlate(group,"Heading","ตัวอย่าง · กลุ่ม "+(c+1),font,2,2,296,54);
                for(int r=0;r<2;r++)InkWidgets.StatRow(group,"Stat_"+(c*2+r),axes[c*2+r],"—",font,10,72+r*62,280,c==0?UiPalette.Jade:c==1?UiPalette.Blue:UiPalette.Vermilion);
            }
            InkWidgets.Text(status,"MockNoteText","ตัวอย่าง — ยังไม่มีสเตตัสจริงในข้อมูลศิษย์",font,28,0,0,924,44,UiPalette.Secondary);
            foreach(var tab in new[]{"EquipmentContent","SkillContent","DestinyContent","SpiritRootContent"})
            {
                var rt=InkWidgets.Rect(host,tab,0,0,924,522);InkWidgets.Stretch(rt);
                InkWidgets.Text(rt,"PlaceholderText","ยังไม่เปิดใช้งาน",font,34,0,0,924,522,UiPalette.Secondary,TextAlignmentOptions.Center);
            }
            var viewport=InkWidgets.Rect(window,"RailViewport",24,712,720,64);viewport.gameObject.AddComponent<RectMask2D>();
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=true;scroll.vertical=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            var content=InkWidgets.Rect(viewport,"Content",0,0,0,60);content.pivot=new Vector2(0,1);
            var layout=content.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=12;layout.childControlWidth=false;layout.childControlHeight=false;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
            var fitter=content.gameObject.AddComponent<ContentSizeFitter>();fitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.content=content;scroll.viewport=viewport;
            InkWidgets.Text(window,"WalletFooter","",font,28,766,712,610,64,UiPalette.Text,TextAlignmentOptions.MidlineRight);
            var view=root.gameObject.AddComponent<DiscipleDetailView>();var so=new SerializedObject(view);
            Set(so,"nameText",nameValue);Set(so,"rankText",rankValue);Set(so,"taskText",taskValue);Set(so,"walletText",walletValue);
            Set(so,"closeButton",close);Set(so,"portraitRenderer",renderer);Set(so,"railContent",content);Set(so,"tabRail",tabs);Set(so,"contentHost",host);Set(so,"headerNameText",name);Set(so,"headerRankText",rank);so.ApplyModifiedPropertiesWithoutUndo();
            var prefab=PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/Prefabs/UI/DiscipleDetailPanel.prefab");Object.DestroyImmediate(root.gameObject);
            var catalog=AssetDatabase.LoadAssetAtPath<UIPanelCatalog>("Assets/Panel Catalog/MainPanelCatalog.asset");var cs=new SerializedObject(catalog);var panels=cs.FindProperty("panels");
            bool found=false;for(int i=0;i<panels.arraySize;i++){var el=panels.GetArrayElementAtIndex(i);if(el.FindPropertyRelative("PanelId").stringValue!="DiscipleDetail")continue;el.FindPropertyRelative("Prefab").objectReferenceValue=prefab;found=true;}
            if(!found){panels.arraySize++;var el=panels.GetArrayElementAtIndex(panels.arraySize-1);el.FindPropertyRelative("PanelId").stringValue="DiscipleDetail";el.FindPropertyRelative("Prefab").objectReferenceValue=prefab;el.FindPropertyRelative("PresenterKind").enumValueIndex=(int)UIPresenterKind.DiscipleDetail;}
            cs.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        }
        private static void Set(SerializedObject so,string name,Object value)=>so.FindProperty(name).objectReferenceValue=value;
    }
}
#endif
