// Play-mode flow verification for Task B + Task D (run OUTSIDE Assets/ via
// run_script while the Editor is PLAYING).
//
// FRAME GOTCHA (cost us a debug round): a Graphic instantiated in the SAME
// frame is not yet raycastable — GraphicRaycaster.Raycast returns 0 hits no
// matter how many Canvas.ForceUpdateCanvases you call (verified empirically:
// same-frame = 0 hits, one frame later = 4 hits). So this is a STATE MACHINE
// driven by EditorApplication.update: open → (wait 2 frames) → probe/click →
// (wait) → verify → … Each step runs in its own frame, exactly like a real
// player clicking across frames.
//
// What it exercises (the REAL paths, not direct component pokes):
//   open  = BottomMenuView.DiscipleButton.onClick (UIBootstrap toggle)
//   close = a synthetic UI click at the coin's visible disc centre, pushed
//           through the canvas GraphicRaycaster + ExecuteEvents hierarchy
//           (exactly what StandaloneInputModule does with a real click)
//   close = DiscipleListView.PressEscape (what Update calls on Esc)
//   × 3 rounds, checking element counts never stack and the roster rebuilds.
//
// Task D checks: viewport 624, grid cell 197x304 (2 rows fill it), content
// grows to 944 with 16 cards (3 rows) so the ScrollRect has room to scroll.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.UI;

public static class DiscipleListTaskBFlow
{
    private static readonly List<string> Problems = new List<string>();
    private static int _checks;
    private static int _round = 1;
    private static int _phase;
    private static int _waitFrames;
    private static int _expectedCards = -1;
    private static int _expectedWindowChildren = -1;
    private static Vector2 _coinPoint = new Vector2(-1f, -1f);
    private static Vector2 _discPoint = new Vector2(-1f, -1f);
    private static EditorApplication.CallbackFunction _tick;

    public static void Main()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[TaskBFlow] must run in PLAY mode");
            return;
        }
        if (_tick != null) return; // already running

        Problems.Clear();
        _checks = 0;
        _round = 1;
        _phase = -1; // pre-step: close any leftover panel
        _waitFrames = 0;

        Debug.Log("[TaskBFlow] starting frame-driven flow test…");
        _tick = Tick;
        EditorApplication.update += _tick;
    }

    private static void Tick()
    {
        if (_waitFrames > 0) { _waitFrames--; return; }

        switch (_phase)
        {
            case -1: PreStep(); break;
            case 0: PhaseOpen(); break;
            case 1: PhaseProbeAndClick(); break;
            case 2: PhaseVerifyClickClosed(); break;
            case 3: PhaseReopenAndEsc(); break;
            case 31: PhaseReopenCheck(); break;
            case 4: PhaseVerifyEscClosed(); break;
            default: Finish(); break;
        }
    }

    // ── phases ─────────────────────────────────────────────────────────────

    private static void PreStep()
    {
        var pre = FindActiveView();
        if (pre != null) pre.PressEscape();
        _phase = 0;
        _waitFrames = 2;
    }

    private static void PhaseOpen()
    {
        var bottomMenu = Object.FindAnyObjectByType<BottomMenuView>();
        if (!Check(bottomMenu != null, "BottomMenuView exists in play scene") ||
            !Check(bottomMenu.DiscipleButton != null, "DiscipleButton wired"))
        { Finish(); return; }

        bottomMenu.DiscipleButton.onClick.Invoke();
        _phase = 1;
        _waitFrames = 2;
    }

    private static void PhaseProbeAndClick()
    {
        var view = FindActiveView();
        if (!Check(view != null, "round " + _round + ": panel opened via DiscipleButton"))
        { Finish(); return; }

        // Layout settles naturally across the 2 waited frames; force one more
        // pass so same-frame-created cards measure final sizes.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.transform as RectTransform);
        Canvas.ForceUpdateCanvases();

        var window = view.transform.Find("Window") as RectTransform;
        Check(window != null, "round " + _round + ": Window exists");

        // No stacking: element counts must be identical every round.
        int cards = CountCards(view);
        int windowChildren = window != null ? window.childCount : -1;
        if (_expectedCards < 0)
        {
            _expectedCards = cards;
            _expectedWindowChildren = windowChildren;
        }
        Check(cards == _expectedCards,
              "round " + _round + ": card count stable (" + cards + " vs " + _expectedCards + ")");
        Check(windowChildren == _expectedWindowChildren,
              "round " + _round + ": window children stable (" + windowChildren + " vs " +
              _expectedWindowChildren + ")");

        if (_round == 1)
        {
            DeepChecks(view);
            _coinPoint = ScreenPointOf(view, new Vector2(0f, 0f));
            _discPoint = ScreenPointOf(view, new Vector2(0f, 30f)); // measured disc face centre
        }

        // Coin clickability: probe BOTH points (raycast only), click ONE at the
        // disc centre — where a player actually clicks the coin.
        string coinHit = ProbeTopHit(_coinPoint);
        Check(coinHit == "CloseHitArea" || coinHit == "CloseMedallion",
              "round " + _round + ": raycast at coin centre hits the close hit area (got '" + coinHit + "')");
        string discHit = ProbeTopHit(_discPoint);
        Check(discHit == "CloseHitArea" || discHit == "CloseMedallion",
              "round " + _round + ": raycast at disc centre hits the close hit area (got '" + discHit + "')");
        ClickAt(_discPoint, "disc centre");

        _phase = 2;
        _waitFrames = 2;
    }

    private static void PhaseVerifyClickClosed()
    {
        var afterClick = FindActiveView();
        Check(afterClick == null, "round " + _round + ": coin click closed the panel");
        if (afterClick != null) afterClick.PressEscape(); // recover for the next asserts

        _phase = 3;
        _waitFrames = 2;
    }

    private static void PhaseReopenAndEsc()
    {
        // Reopen through the toggle. If the toggle state desynced anywhere
        // above, THIS click would close-nothing instead of opening — caught here.
        var bottomMenu = Object.FindAnyObjectByType<BottomMenuView>();
        bottomMenu.DiscipleButton.onClick.Invoke();
        _phase = 31;
        _waitFrames = 2;
    }

    private static void PhaseReopenCheck()
    {
        var view2 = FindActiveView();
        Check(view2 != null, "round " + _round + ": panel reopened for Esc test");
        if (view2 != null)
        {
            Check(CountCards(view2) == _expectedCards, "round " + _round + ": roster rebuilt after reopen");
            view2.PressEscape();
        }
        _phase = 5;
        _waitFrames = 2;
    }

    private static void PhaseVerifyEscClosed()
    {
        Check(FindActiveView() == null, "round " + _round + ": Esc (PressEscape) closed the panel");

        if (_round >= 3) { _phase = 99; return; }
        _round++;
        _phase = 0; // next round's open
        _waitFrames = 2;
    }

    private static void Finish()
    {
        EditorApplication.update -= _tick;
        _tick = null;

        if (Problems.Count == 0)
            Debug.Log("[TaskBFlow] ALL " + _checks + " CHECKS PASSED — coin raycast (coin+disc centre), click closes, Esc closes, " +
                      "3 rounds no stacking; cards=" + _expectedCards +
                      " coin@(" + _coinPoint.x.ToString("F0") + "," + _coinPoint.y.ToString("F0") + ")");
        else
            Debug.LogError("[TaskBFlow] " + Problems.Count + " PROBLEM(S): " + string.Join(" | ", Problems));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static DiscipleListView FindActiveView()
    {
        var views = Object.FindObjectsByType<DiscipleListView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return views.Length > 0 ? views[0] : null;
    }

    private static int CountCards(DiscipleListView view)
    {
        int n = 0;
        for (int i = 0; i < view.CardsRoot.childCount; i++)
            if (view.CardsRoot.GetChild(i).GetComponent<DiscipleListCard>() != null) n++;
        return n;
    }

    /// <summary>Overlay canvas: a child's world position IS a screen point,
    /// so transform the medallion-local point straight to screen space.</summary>
    private static Vector2 ScreenPointOf(DiscipleListView view, Vector2 medallionLocal)
    {
        var medallion = view.transform.Find("Window/CloseMedallion") as RectTransform;
        if (medallion == null) return new Vector2(-1f, -1f);
        Vector3 world = medallion.TransformPoint(new Vector3(medallionLocal.x, medallionLocal.y, 0f));
        return new Vector2(world.x, world.y);
    }

    /// <summary>Raycast every canvas at a screen point; returns the top hit's
    /// name (no click — probing must have zero side effects).</summary>
    private static string ProbeTopHit(Vector2 screenPoint)
    {
        var es = Object.FindAnyObjectByType<EventSystem>();
        if (es == null) return "<no EventSystem>";

        var ped = new PointerEventData(es) { position = screenPoint };
        var results = new List<RaycastResult>();
        foreach (var raycaster in Object.FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None))
            raycaster.Raycast(ped, results);
        if (results.Count == 0) return "<no hit>";
        results.Sort((a, b) =>
        {
            int byOrder = b.sortingOrder.CompareTo(a.sortingOrder); // higher canvas first
            return byOrder != 0 ? byOrder : b.depth.CompareTo(a.depth); // then topmost graphic
        });
        return results[0].gameObject.transform.name;
    }

    /// <summary>Pushes a synthetic click at a screen point through the canvas
    /// raycaster + event hierarchy — the same path StandaloneInputModule uses.</summary>
    private static void ClickAt(Vector2 screenPoint, string what)
    {
        var es = Object.FindAnyObjectByType<EventSystem>();
        if (!Check(es != null, "EventSystem exists for " + what + " click")) return;

        var ped = new PointerEventData(es) { position = screenPoint };
        var results = new List<RaycastResult>();
        foreach (var raycaster in Object.FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None))
            raycaster.Raycast(ped, results);
        if (!Check(results.Count > 0, "raycast hit something at " + what)) return;
        results.Sort((a, b) =>
        {
            int byOrder = b.sortingOrder.CompareTo(a.sortingOrder);
            return byOrder != 0 ? byOrder : b.depth.CompareTo(a.depth);
        });

        var first = results[0].gameObject;
        Check(first.transform.name == "CloseHitArea" || first.transform.name == "CloseMedallion",
              "top hit at " + what + " is the close hit area (got '" + first.transform.name + "')");
        var handled = ExecuteEvents.ExecuteHierarchy(first, ped, ExecuteEvents.pointerClickHandler);
        Check(handled != null, "click at " + what + " reached a pointerClickHandler");
    }

    /// <summary>Round-1 one-time checks: missing scripts, coin button wiring,
    /// text overflow settings, Task D grid sizing + scroll headroom.</summary>
    private static void DeepChecks(DiscipleListView view)
    {
        // Missing scripts (Task B): the live instance must have NONE.
        foreach (var t in view.GetComponentsInChildren<Transform>(true))
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            Check(missing == 0, "missing scripts on " + GetPath(t) + " = " + missing);
        }

        // Coin button wiring.
        var medallion = view.transform.Find("Window/CloseMedallion") as RectTransform;
        Check(medallion != null, "CloseMedallion exists");
        if (medallion == null) return;
        var medImg = medallion.GetComponent<Image>();
        var medBtn = medallion.GetComponent<Button>();
        Check(medImg != null && medImg.raycastTarget, "coin image raycastable");
        Check(medImg != null && medImg.enabled, "coin image enabled");
        Check(medBtn != null && medBtn.targetGraphic == medImg, "close Button targetGraphic = coin image");
        var hit = medallion.Find("CloseHitArea") as RectTransform;
        Check(hit != null, "CloseHitArea exists");
        if (hit != null)
        {
            var hitImg = hit.GetComponent<Image>();
            Check(hitImg != null && hitImg.raycastTarget, "CloseHitArea raycastable");
            Check(Mathf.Abs(hit.anchoredPosition.x) < 0.5f && Mathf.Abs(hit.anchoredPosition.y) < 0.5f,
                  "CloseHitArea centred (was offset +57.2 — the click-miss bug)");
        }
        var label = medallion.Find("CloseLabel");
        var labelTmp = label != null ? label.GetComponent<TMPro.TextMeshProUGUI>() : null;
        Check(labelTmp != null && labelTmp.text == "ปิด", "'ปิด' label on the coin");
        Check(labelTmp == null || !labelTmp.raycastTarget, "label never blocks clicks");

        // Text overflow (Task B): ellipsis, never a hard mid-word clip.
        // Task C structure: Icon lives under CardTop now.
        var firstCard = view.CardsRoot.childCount > 0 ? view.CardsRoot.GetChild(0) : null;
        if (Check(firstCard != null, "at least one card to inspect texts"))
        {
            var iconT = firstCard.Find("CardTop/Icon")?.GetComponent<UnityEngine.UI.Image>();
            Check(iconT != null, "Icon under CardTop (Task C structure)");
            var nameT = firstCard.Find("Name")?.GetComponent<TMPro.TextMeshProUGUI>();
            var detailT = firstCard.Find("StatBand/Detail")?.GetComponent<TMPro.TextMeshProUGUI>();
            var walletT = firstCard.Find("StatBand/Wallet")?.GetComponent<TMPro.TextMeshProUGUI>();
            Check(nameT != null && nameT.overflowMode == TMPro.TextOverflowModes.Ellipsis,
                  "Name overflow = Ellipsis");
            Check(nameT == null || nameT.textWrappingMode == TMPro.TextWrappingModes.NoWrap,
                  "Name single-line (NoWrap)");
            Check(detailT != null && detailT.overflowMode == TMPro.TextOverflowModes.Ellipsis,
                  "Detail/task overflow = Ellipsis");
            Check(detailT == null || detailT.textWrappingMode == TMPro.TextWrappingModes.Normal,
                  "Detail word-wrap on");
            Check(walletT != null && walletT.overflowMode == TMPro.TextOverflowModes.Ellipsis,
                  "Wallet overflow = Ellipsis");
        }

        // Task D: viewport 624, cell 197x304 → 2 rows (608+16) exactly fill it.
        var viewport = view.CardsRoot.parent as RectTransform;
        Check(viewport != null && Mathf.Abs(viewport.rect.height - 624f) < 0.5f,
              "viewport height 624 (actual " + (viewport != null ? viewport.rect.height.ToString("F1") : "null") + ")");
        var grid = view.CardsRoot.GetComponent<FitWidthGridLayoutGroup>();
        Check(grid != null, "grid component on Content (not a missing script)");
        if (grid != null)
        {
            Check(Mathf.Abs(grid.cellSize.x - 197f) < 1.5f,
                  "cell width 197 (actual " + grid.cellSize.x + ")");
            Check(Mathf.Abs(grid.cellSize.y - 304f) < 1.5f,
                  "cell height 304 = (624-16)/2, 2 rows fill viewport (actual " + grid.cellSize.y + ")");
            if (viewport != null)
                Check(Mathf.Abs(grid.cellSize.y * 2f + grid.spacing.y - viewport.rect.height) < 1.5f,
                      "2 rows exactly fill the viewport height (Task D)");
        }

        // ── Task C: tier frames + numbered corner badges ─────────────────
        CheckTierBadges(view);

        // Scroll headroom: 12 extra cards → 16 total = 3 rows = 944 > 624.
        var created = new List<DiscipleListCard>();
        for (int i = 0; i < 12; i++)
        {
            var c = view.CreateCard();
            if (c != null) created.Add(c);
        }
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.CardsRoot);
        Canvas.ForceUpdateCanvases();
        float contentH = view.CardsRoot.rect.height;
        Check(Mathf.Abs(contentH - (304f * 3f + 16f * 2f)) < 1.5f,
              "16 cards → content 944 (3 rows, scrollable) actual " + contentH);
        var sr = view.GetComponentInChildren<ScrollRect>(true);
        Check(sr != null && sr.vertical, "ScrollRect.vertical on (scrolling works)");
        // DestroyImmediate: play-mode Object.Destroy is deferred to frame end,
        // which would corrupt the per-round child counts in this same frame.
        foreach (var c in created) if (c != null) Object.DestroyImmediate(c.gameObject);
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Task C: every card got ApplyTier(rank) + SetBadge(order) —
    /// frame/body/badge dot share one colour per rank and the dot shows the
    /// 1-based roster order. Mock roster: d000 SectMaster(purple), d001 Outer
    /// (orange), d002 Inner(blue-grey), d003 Elder(green).</summary>
    private static void CheckTierBadges(DiscipleListView view)
    {
        var expected = new[]
        {
            (DiscipleRank.SectMaster, "1"),
            (DiscipleRank.OuterDisciple, "2"),
            (DiscipleRank.InnerDisciple, "3"),
            (DiscipleRank.Elder, "4"),
        };
        int n = Mathf.Min(expected.Length, view.CardsRoot.childCount);
        Check(view.CardsRoot.childCount >= 4,
              "roster has 4+ cards for tier checks (actual " + view.CardsRoot.childCount + ")");
        for (int i = 0; i < n; i++)
        {
            var card = view.CardsRoot.GetChild(i).GetComponent<DiscipleListCard>();
            if (!Check(card != null, "card " + i + " has DiscipleListCard")) continue;
            var (rank, order) = expected[i];
            Color want = DiscipleListCard.TierColor(rank);
            Check(ColorDistance(card.BodyColor, want) < 0.01f,
                  "card " + i + " (" + rank + ") body = tier colour (got " + card.BodyColor + " want " + want + ")");
            Check(ColorDistance(card.BadgeColor, want) < 0.01f,
                  "card " + i + " badge dot = tier colour");
            Check(card.BadgeText == order,
                  "card " + i + " badge shows roster order " + order + " (got '" + card.BadgeText + "')");
        }
    }

    private static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db + da * da);
    }

    private static string GetPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        var p = t.parent;
        while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
        return sb.ToString();
    }

    private static bool Check(bool ok, string label)
    {
        _checks++;
        if (!ok) Problems.Add(label);
        return ok;
    }
}
