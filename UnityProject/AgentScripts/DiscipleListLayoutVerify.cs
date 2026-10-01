// Editor-only verification for the DiscipleList layout pass (run OUTSIDE Assets/
// via run_script so it never triggers an asset import or domain reload).
//
// Checks (numbers expected from the generator's measured constants):
//   window 1400x790 centred · ContentArea insets L69/R66/T120/B46 · viewport 1265x624
//   grid cells (1265-5*16)/6 = 197 wide, height = max(aspect 253, (624-16)/2 = 304)
//   = 304 (Task D: 2 rows fill the viewport) · 4 cards on one row
//   posts centred at x=±690 covering 654..726 (outside cards) · medallion on the
//   left post · the COIN is the button (targetGraphic = medallion image, hit area
//   centred, "ปิด" label) · close button click fires the view's CloseClicked event
//   · PressEscape fires it too (Task B: Esc closes the panel).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.UI;

public static class DiscipleListLayoutVerify
{
    private static readonly List<string> Problems = new List<string>();
    private static int _checks;

    public static void Main()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/DiscipleListPanel.prefab");
        Check(prefab != null, "prefab exists");

        var canvasGo = new GameObject("VerifyCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        var canvasRt = canvasGo.GetComponent<RectTransform>();

        var view = default(DiscipleListView);
        try
        {
            // ── 3 open/close rounds (acceptance: nothing stacks up) ──
            int round1ChildCount = -1;
            for (int round = 1; round <= 3; round++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasRt);
                view = inst.GetComponent<DiscipleListView>();
                view.Show();
                // Edit-mode gotcha: anchor-stretch sizes settle only when the canvas
                // updates; force it, then rebuild the layout subtree twice so the
                // grid sees its final width and the cards lay out their children.
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(inst.transform as RectTransform);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(inst.transform as RectTransform);

                var presenterStub = inst.AddComponent<CountingPresenterStub>();
                var card = view.CreateCard();
                if (card == null) Problems.Add("CreateCard returned null");
                else
                {
                    int before = CountCards(view);
                    card.Set("v1", "ทดสอบ", "· ภารกิจ", "123", null);
                    int fired = 0;
                    System.Action<string> onClick = _ => fired++;
                    card.Clicked += onClick;
                    card.Click(); // presenter flow: click → DiscipleSelectedMessage
                    card.Clicked -= onClick;
                    Check(fired == 1, "round " + round + ": card click fires Clicked once");
                    Check(CountCards(view) == before, "round " + round + ": card count stable");
                }

                // The card was created after the rebuilds above — give it its
                // own layout pass before measuring (same edit-mode gotcha).
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(inst.transform as RectTransform);
                Canvas.ForceUpdateCanvases();

                if (round == 1) MeasureLayout(inst, view);
                round1ChildCount = round == 1 ? CountCards(view) : round1ChildCount;

                // Wire the close path exactly like the view does, then click it.
                int closed = 0;
                System.Action onClose = () => closed++;
                view.CloseClicked += onClose;
                var medallion = inst.transform.Find("Window/CloseMedallion").gameObject;
                var medButton = medallion.GetComponent<Button>();
                Check(medButton != null, "round " + round + ": CloseMedallion itself carries the Button");
                bool clicked = false;
                if (medButton != null)
                {
                    Check(medButton.targetGraphic == medallion.GetComponent<Image>(),
                          "round " + round + ": close button targetGraphic = the coin image");
                    medButton.onClick.Invoke();
                    clicked = true;
                }
                Check(clicked && closed == 1, "round " + round + ": medallion close button fires CloseClicked");

                // Task B: Esc path — PressEscape is exactly what Update calls
                // on Input.GetKeyDown(Escape).
                view.PressEscape();
                Check(closed == 2, "round " + round + ": PressEscape fires CloseClicked");
                view.CloseClicked -= onClose;

                Object.DestroyImmediate(inst);
                view = null;
            }

            // Nothing stacked up: final instantiation has no leftovers in scene
            var leftovers = Object.FindObjectsByType<DiscipleListView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check(leftovers.Length == 0, "no leftover panels after 3 open/close rounds");
        }
        finally
        {
            if (view != null && view.gameObject != null) Object.DestroyImmediate(view.gameObject);
            Object.DestroyImmediate(canvasGo);
        }

        if (Problems.Count == 0)
            Debug.Log("[DiscipleListLayoutVerify] ALL " + _checks + " CHECKS PASSED");
        else
            Debug.LogError("[DiscipleListLayoutVerify] " + Problems.Count + " PROBLEM(S): " +
                           string.Join(" | ", Problems));
    }

    private static void MeasureLayout(GameObject inst, DiscipleListView view)
    {
        var windowRt = inst.transform.Find("Window") as RectTransform;
        Check(windowRt != null, "Window exists");
        if (windowRt == null) return;
        Check(Mathf.Abs(windowRt.rect.width - 1400f) < 0.5f && Mathf.Abs(windowRt.rect.height - 790f) < 0.5f,
              "window 1400x790 (actual " + windowRt.rect.width + "x" + windowRt.rect.height + ")");

        var areaRt = windowRt.Find("ContentArea") as RectTransform;
        Check(areaRt != null, "ContentArea exists");
        if (areaRt != null)
        {
            Check(Mathf.Abs(areaRt.rect.width - 1265f) < 0.5f && Mathf.Abs(areaRt.rect.height - 624f) < 0.5f,
                  "content area 1265x624 = window - insets L69/R66/T120/B46 (actual " +
                  areaRt.rect.width + "x" + areaRt.rect.height + ")");
        }

        // Posts: cover 654..726 on each side — outside the card grid (max |x| 614).
        // The rod Image is rotated 90°, so world corners give the true footprint.
        foreach (var rodName in new[] { "RodLeft", "RodRight" })
        {
            var rod = windowRt.Find(rodName) as RectTransform;
            if (!Check(rod != null, rodName + " exists")) continue;
            var corners = new Vector3[4];
            rod.GetWorldCorners(corners);
            // Canvas scale = 1 here; the rod is rotated 90°, so min/max must span
            // ALL four corners (c0/c2 alone straddle the wrong diagonal).
            float minX = corners[0].x, maxX = corners[0].x;
            for (int i = 1; i < 4; i++) { minX = Mathf.Min(minX, corners[i].x); maxX = Mathf.Max(maxX, corners[i].x); }
            float centerX = (minX + maxX) / 2f;
            float innerEdge = Mathf.Abs(centerX) - (maxX - minX) / 2f;
            Check(innerEdge >= 613.5f,
                  rodName + " inner edge " + innerEdge.ToString("F1") + " >= 614 (outside card grid)");
        }

        var med = windowRt.Find("CloseMedallion") as RectTransform;
        Check(med != null, "CloseMedallion exists");
        if (med != null)
        {
            Check(Mathf.Abs(med.anchoredPosition.x - (-690f)) < 0.5f,
                  "medallion on the left post (x=" + med.anchoredPosition.x + ")");
            var btn = med.GetComponent<Button>();
            Check(btn != null, "close Button is ON the medallion");
            var medImg = med.GetComponent<Image>();
            Check(medImg != null && medImg.raycastTarget, "medallion coin image is raycastable");
            var hit = med.Find("CloseHitArea") as RectTransform;
            Check(hit != null, "enlarged CloseHitArea exists");
            if (hit != null)
            {
                var hitImg = hit.GetComponent<Image>();
                Check(hitImg != null && hitImg.raycastTarget, "CloseHitArea is raycastable");
                Check(Mathf.Abs(hit.anchoredPosition.x) < 0.5f && Mathf.Abs(hit.anchoredPosition.y) < 0.5f,
                      "CloseHitArea centred on the art (no dead zone above the coin)");
                Check(hit.sizeDelta.x >= 90f && hit.sizeDelta.y >= 150f,
                      "CloseHitArea covers the whole coin (actual " + hit.sizeDelta + ")");
            }
            var lbl = med.Find("CloseLabel");
            var lblTmp = lbl != null ? lbl.GetComponent<TMPro.TextMeshProUGUI>() : null;
            Check(lblTmp != null && lblTmp.text == "ปิด", "close label 'ปิด' on the coin");
            Check(lblTmp == null || !lblTmp.raycastTarget, "close label is not a raycast target");
        }

        var plate = windowRt.Find("TitlePlate") as RectTransform;
        Check(plate != null, "TitlePlate exists");
        if (plate != null)
        {
            Check(Mathf.Abs(plate.anchoredPosition.x) < 0.5f, "title plate centred");
            var tt = plate.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            Check(tt != null && tt.alignment == TMPro.TextAlignmentOptions.Center,
                  "title text centred on the plate");
        }

        // Grid: cells (1265 - 5*16)/6 = 197 wide, height = max(aspect 253,
        // (viewport 624 - 16) / 2 rows = 304) = 304 (Task D: 2 rows fill 624).
        var grid = view.CardsRoot.GetComponent<FitWidthGridLayoutGroup>();
        Check(grid != null, "cardsRoot has FitWidthGridLayoutGroup");
        Check(grid.Columns == 6, "grid columns = 6");
        Check(grid.RowsInView == 2, "grid RowsInView = 2");
        Check(Mathf.Abs(grid.cellSize.x - 197f) < 1.5f && Mathf.Abs(grid.cellSize.y - 304f) < 1.5f,
              "grid cell 197x304 (actual " + grid.cellSize.x + "x" + grid.cellSize.y + ")");
        // Task D reference height is the VIEWPORT (Content's parent), not the
        // Content rect itself (ContentSizeFitter makes Content height = rows).
        var viewportRt = view.CardsRoot.parent as RectTransform;
        Check(viewportRt != null && Mathf.Abs(viewportRt.rect.height - 624f) < 0.5f,
              "viewport height 624 (actual " + (viewportRt != null ? viewportRt.rect.height.ToString("F1") : "null") + ")");
        Check(viewportRt != null &&
              Mathf.Abs(grid.cellSize.y * 2f + grid.spacing.y - viewportRt.rect.height) < 1.5f,
              "2 rows exactly fill the viewport height (Task D)");
        var cardsRootRt = view.CardsRoot;
        Check(Mathf.Abs(cardsRootRt.rect.width - 1265f) < 1.5f,
              "cardsRoot width = viewport 1265 (actual " + cardsRootRt.rect.width + ")");            // Card children must not overlap: portrait above name above stat band.
            // Task C structure: Icon lives under CardTop.
            var card = view.CardsRoot.GetComponentInChildren<DiscipleListCard>(true);
            Check(card != null, "card exists for measurement");
            if (card != null)
            {
                var cardRt = card.GetComponent<RectTransform>();
                var icon = cardRt.Find("CardTop/Icon") as RectTransform;
                var name = cardRt.Find("Name") as RectTransform;
                var band = cardRt.Find("StatBand") as RectTransform;
                Check(icon != null && name != null && band != null, "card has CardTop/Icon/Name/StatBand");
                if (icon != null && name != null && band != null)
                {
                    Check(name.anchoredPosition.y < icon.anchoredPosition.y - 20f,
                          "name below portrait");
                    Check(band.anchoredPosition.y < name.anchoredPosition.y,
                          "stat band below name");
                }
                var img = card.GetComponent<Image>();
                Check(img != null && img.color.a > 0.9f, "card body tintable (frame colour alpha)");

                // Task C: badge exists, tier API no-ops safely.
                var badge = cardRt.Find("TierBadge");
                Check(badge != null, "TierBadge exists on the card");
                if (badge != null)
                {
                    var dot = badge.Find("BadgeDot")?.GetComponent<Image>();
                    var num = badge.Find("BadgeNum")?.GetComponent<TMPro.TextMeshProUGUI>();
                    Check(dot != null && num != null, "badge has BadgeDot + BadgeNum");
                    card.ApplyTier(DiscipleRank.Elder);
                    Check(card.BadgeColor == DiscipleListCard.TierColor(DiscipleRank.Elder),
                          "ApplyTier recolours the badge dot");
                    card.SetBadge(7);
                    Check(card.BadgeText == "7", "SetBadge writes the order numeral");
                    card.SetBadge(0);
                    Check(!badge.gameObject.activeSelf, "SetBadge(0) hides the badge");
                    card.SetBadge(1);
                }
            }

        Debug.Log("[DiscipleListLayoutVerify] window=" + windowRt.rect.width + "x" + windowRt.rect.height +
                  " contentArea=" + (areaRt != null ? areaRt.rect.width + "x" + areaRt.rect.height : "?") +
                  " cell=" + (grid != null ? grid.cellSize.x + "x" + grid.cellSize.y : "?") +
                  " columns=6 spacing=16 rowsInView=2 cardAspect=290:225");
    }

    private static int CountCards(DiscipleListView view)
    {
        int n = 0;
        for (int i = 0; i < view.CardsRoot.childCount; i++)
            if (view.CardsRoot.GetChild(i).GetComponent<DiscipleListCard>() != null) n++;
        return n;
    }

    private static bool Check(bool ok, string label)
    {
        _checks++;
        if (!ok) Problems.Add(label);
        return ok;
    }

    // CreateCard only needs the view; the presenter is not part of this check.
    private sealed class CountingPresenterStub : MonoBehaviour { }
}
