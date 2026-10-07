using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VContainer;
using Xianxia.Sect;
using Xianxia.Sect.UI;
using Xianxia.Sect.Messages;

public static class VerifyInkUi
{
    static UIService service;
    static int step, wait, round, errors, missingWarnings;
    static readonly List<string> failures = new List<string>();
    static readonly List<string> checks = new List<string>();
    static readonly List<string> rects = new List<string>();
    static int[] railIds;
    static int detailCount;
    static int repeatSwitch;
    static bool done;
    static double nextTick;
    public static string Main()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play mode required");
        service = (UIService)GameLifetimeScope.Injector.Resolve(typeof(UIService));
        var existingList = List();
        if (existingList != null) existingList.PressEscape();
        service.Close("DiscipleDetail"); service.Close("EventPopup");
        failures.Clear(); checks.Clear(); rects.Clear(); round=0;step=0;wait=4;errors=0;missingWarnings=0;repeatSwitch=0;done=false;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
        return "Frame-driven v2 verification started; report: art/ink_ui_verification.json";
    }
    static void Log(string message,string stack,LogType type)
    {
        if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors++;
        if(message.Contains("[InkUI] Missing Resources/Avatar/Icons/icon_verify_missing_v3"))missingWarnings++;
    }
    static void Check(bool ok,string description) { checks.Add(description+": "+ok);if(!ok)failures.Add(description); }
    static DiscipleListView List()=>UnityEngine.Object.FindAnyObjectByType<DiscipleListView>();
    static DiscipleDetailView Detail()=>UnityEngine.Object.FindAnyObjectByType<DiscipleDetailView>();
    static Button ButtonAt(Component view,string path)=>view.transform.Find(path).GetComponent<Button>();
    static Rect Bounds(RectTransform rt,Transform relative)
    {
        var corners=new Vector3[4];rt.GetWorldCorners(corners);
        var points=corners.Select(p=>relative.InverseTransformPoint(p)).ToArray();
        return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
    }
    static void Measure(Component view)
    {
        var window=view.transform.Find("Window") as RectTransform;
        var w=window.rect;
        Check(Mathf.Abs(w.width-1400)<.01f && Mathf.Abs(w.height-790)<.01f,"window "+view.name+" 1400x790");
        string[] paths=view is DiscipleListView?new[]{"TitlePlate","CloseButton","Search","SortButton","Viewport","Footer"}:new[]{"TabRail","CloseButton","PortraitColumn","Header","ContentHost","RailViewport","WalletFooter"};
        var bounds=new Dictionary<string,Rect>();
        foreach(var path in paths)
        {
            var b=Bounds(window.Find(path) as RectTransform,window);bounds[path]=b;
            rects.Add(view.GetType().Name+"/"+path+" "+b);
            Check(w.Contains(b.min) && w.Contains(b.max),path+" inside window");
        }
        for(int i=0;i<paths.Length;i++)for(int j=i+1;j<paths.Length;j++)
            Check(!bounds[paths[i]].Overlaps(bounds[paths[j]]),paths[i]+" does not overlap "+paths[j]);
        foreach(var t in view.GetComponentsInChildren<Transform>(true))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"no missing script "+t.name);
        foreach(var text in view.GetComponentsInChildren<TMP_Text>())
        {
            Check(text.fontSize>=26,"font >=26 "+text.name);
            Check(!text.isTextTruncated,"not truncated "+text.name+" "+text.text);
            Check(text.font!=null && text.font.name.Contains("THSarabun"),"Thai font "+text.name);
        }
    }
    static void Tick()
    {
        if(done)return;
        if(wait > 0) { nextTick=EditorApplication.timeSinceStartup + .3; wait=0; return; }
        if(EditorApplication.timeSinceStartup < nextTick) return;
        try
        {
            switch(step++)
            {
                case 0:
                    UnityEngine.Object.FindAnyObjectByType<BottomMenuView>().DiscipleButton.onClick.Invoke();wait=5;break;
                case 1:
                    Check(List()!=null,"round "+round+" list opened through bottom menu");
                    if(List()==null)throw new Exception("Missing list");
                    Canvas.ForceUpdateCanvases();
                    Check(List().CardsRoot.childCount==4,"round "+round+" four cards");
                    Check(List().CardsRoot.GetComponent<FitWidthGridLayoutGroup>().cellSize==new Vector2(236,316),"fixed 236x316 cells");
                    if(round==0)Measure(List());
                    var card=List().CardsRoot.GetComponentsInChildren<DiscipleListCard>().First(c=>c.DiscipleId=="d002");
                    card.GetComponent<Button>().onClick.Invoke();wait=5;break;
                case 2:
                    Check(Detail()!=null,"detail opened by card Button");
                    Check(Detail().transform.Find("Window/Header/Label").GetComponent<TMP_Text>().text=="Su Yan","clicked d002 binds Su Yan");
                    Canvas.ForceUpdateCanvases();
                    if(round==0)Measure(Detail());
                    var items=Detail().GetComponentsInChildren<DiscipleDetailView.RailItem>();
                    Check(items.Length==4,"four rail items");
                    if(round==0){railIds=items.Select(i=>i.GetInstanceID()).ToArray();detailCount=Detail().GetComponentsInChildren<Transform>(true).Count(t=>t.GetComponent<TMP_SubMeshUI>()==null);}
                    else Check(items.Select(i=>i.GetInstanceID()).SequenceEqual(railIds),"rail instances reused round "+round);
                    Check(items.All(i=>i.GetComponentsInChildren<AvatarRenderer>(true).Length==0),"rail does not use AvatarRenderer");
                    foreach(var item in items)
                    {
                        var mask=item.transform.Find("CircleMask") as RectTransform;
                        Check(mask.rect.size==new Vector2(56,56),"rail icon 56px "+item.DiscipleId);
                        Check(item.transform.Find("CircleMask/Icon").GetComponent<Image>().preserveAspect,"rail preserveAspect");
                    }
                    ButtonAt(Detail(),"Window/TabRail/Tab_1").onClick.Invoke();wait=4;break;
                case 3:
                    Check(Detail().GetTabContent(DiscipleTab.Status).gameObject.activeSelf,"status tab selected");
                    Check(Detail().GetTabContent(DiscipleTab.Status).GetComponentInChildren<RadarChartGraphic>(true)==null,"no radar in detail");
                    Check(Detail().GetTabContent(DiscipleTab.Status).Find("MockNoteText").GetComponent<TMP_Text>().text.Contains("ตัวอย่าง"),"mock explicitly labelled");
                    Check(Detail().GetTabContent(DiscipleTab.Status).GetComponentsInChildren<TMP_Text>().Where(t=>t.name=="Value").Count()==6,"six example stat values");
                    foreach(var t in Detail().GetTabContent(DiscipleTab.Status).GetComponentsInChildren<TMP_Text>())Check(!t.isTextTruncated,"status not truncated "+t.text);
                    var item1=Detail().GetComponentsInChildren<DiscipleDetailView.RailItem>().First(i=>i.DiscipleId=="d001");
                    item1.GetComponent<Button>().onClick.Invoke();wait=4;break;
                case 4:
                    Check(Detail().transform.Find("Window/Header/Label").GetComponent<TMP_Text>().text=="Lin Feng","rail switches disciple");
                    if (round == 0 && repeatSwitch == 0) detailCount = Detail().GetComponentsInChildren<Transform>(true).Count(t=>t.GetComponent<TMP_SubMeshUI>()==null);
                    else Check(Detail().GetComponentsInChildren<Transform>(true).Count(t=>t.GetComponent<TMP_SubMeshUI>()==null)==detailCount,"detail child count stable after layered pool warm-up");
                    if (repeatSwitch++ < 2)
                    {
                        var target = Detail().GetComponentsInChildren<DiscipleDetailView.RailItem>().First(i=>i.DiscipleId=="d002");
                        target.GetComponent<Button>().onClick.Invoke(); step=3; wait=4; break;
                    }
                    repeatSwitch=0;
                    ButtonAt(Detail(),"Window/TabRail/Tab_2").onClick.Invoke();wait=3;break;
                case 5:
                    Check(Detail().GetTabContent(DiscipleTab.Equipment).GetComponentInChildren<TMP_Text>().text=="ยังไม่เปิดใช้งาน","unavailable tab exact text");
                    var action=ButtonAt(Detail(),"Window/PortraitColumn/AssignTask");
                    Check(!action.interactable,"action disabled");
                    ExecuteEvents.Execute(action.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
                    Check(action.transform.Find("Tooltip").gameObject.activeSelf,"disabled action tooltip visible");
                    ExecuteEvents.Execute(action.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerExitHandler);
                    if(round==0)
                    {
                        var pub=(MessagePipe.IPublisher<WorldEventTriggeredMessage>)GameLifetimeScope.Injector.Resolve(typeof(MessagePipe.IPublisher<WorldEventTriggeredMessage>));
                        pub.Publish(new WorldEventTriggeredMessage{EventId="verify_ink_pending",RequiresDecision=true,Description="UI verification pending event"});
                    }
                    ButtonAt(Detail(),"Window/CloseButton").onClick.Invoke();wait=3;break;
                case 6:
                    Check(Detail()==null,"detail hidden via X");
                    Check(DiscipleModalScope.IsOpen,"list still blocks pending popup");
                    Check(UnityEngine.Object.FindAnyObjectByType<EventPopupView>()==null,"pending event not shown over list");
                    List().PressEscape();wait=4;break;
                case 7:
                    Check(List()==null,"list closed through Esc callback");Check(!DiscipleModalScope.IsOpen,"modal visibility count reset");
                    if(round==0)
                    {
                        var popup=UnityEngine.Object.FindAnyObjectByType<EventPopupView>();
                        Check(popup!=null,"queued event displayed after both modals close");
                        if(popup!=null)Check(popup.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="verify_ink_pending"),"correct pending event preserved");
                        service.Close("EventPopup");
                    }
                    // Bottom menu toggle state is reset by its close callback.
                    if(++round<3){step=0;wait=4;}else{step=8;wait=4;}break;
                case 8:
                    UnityEngine.Object.FindAnyObjectByType<BottomMenuView>().DiscipleButton.onClick.Invoke();wait=4;break;
                case 9:
                    var list=List();var input=list.GetComponentInChildren<TMP_InputField>();input.text="Su Yan";
                    Check(list.CardsRoot.GetComponentsInChildren<DiscipleListCard>().Length==1,"search filters to one card");input.text="";
                    ButtonAt(list,"Window/SortButton").onClick.Invoke();
                    var fallbackCard=list.CardsRoot.GetComponentsInChildren<DiscipleListCard>().First(c=>c.DiscipleId=="d001");
                    Check(fallbackCard.transform.Find("PortraitFallback").gameObject.activeSelf,"no-override disciple uses layered fallback");
                    var railTest=DiscipleDetailView.RailItem.Create(list.transform as RectTransform);
                    railTest.Bind("verify_missing_v3",null,DiscipleSex.Unspecified,null);railTest.Bind("verify_missing_v3",null,DiscipleSex.Unspecified,null);
                    Check(!railTest.transform.Find("CircleMask/Icon").GetComponent<Image>().enabled,"missing icon uses solid circle fallback");
                    Check(missingWarnings==1,"missing icon warns once");UnityEngine.Object.Destroy(railTest.gameObject);
                    var hud=UnityEngine.Object.FindAnyObjectByType<WalletHudView>();Check(hud.GetComponent<Canvas>().overrideSorting && hud.GetComponent<Canvas>().sortingOrder==20,"HUD sorting order 20");
                    var uiRoot=hud.transform.parent;var hudRect=Bounds(hud.transform as RectTransform,uiRoot);var listWindow=Bounds(list.transform.Find("Window") as RectTransform,uiRoot);
                    Check(!hudRect.Overlaps(listWindow),"HUD does not overlap list frame");
                    rects.Add("HUD "+hudRect);
                    var su=list.CardsRoot.GetComponentsInChildren<DiscipleListCard>().First(c=>c.DiscipleId=="d002");su.GetComponent<Button>().onClick.Invoke();wait=4;break;
                case 10:
                    ButtonAt(Detail(),"Window/TabRail/Tab_0").onClick.Invoke();
                    Check(errors==0,"new console errors zero");Finish();break;
            }
        }
        catch(Exception ex){failures.Add(ex.ToString());Finish();}
    }
    static void Finish()
    {
        done=true;EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/ink_ui_verification.json"));
        var report=new Report{checks=checks.ToArray(),failures=failures.ToArray(),rects=rects.ToArray(),errors=errors,rounds=round};
        File.WriteAllText(path,JsonUtility.ToJson(report,true));
        Debug.Log("[InkUI verification] "+checks.Count+" checks, "+failures.Count+" failures; "+path);
    }
    [Serializable]public class Report { public string[] checks,failures,rects;public int errors,rounds; }
}
