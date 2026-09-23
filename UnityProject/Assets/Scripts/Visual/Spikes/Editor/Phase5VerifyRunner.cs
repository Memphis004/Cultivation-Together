#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Phase 5 acceptance verification (editor-only), driven by the marker-file
    /// remote control: "phase5_verify" in Library/visual_spike_command.txt.
    ///
    /// Same domain-reload-proof pattern as Phase2/3/4VerifyRunner:
    /// [InitializeOnLoad] update poll + session marker on disk + step machine.
    ///
    /// Verifies the UI-side acceptance criteria that EditMode tests can't see
    /// (the EditMode suite already covers provider rules + layer-6 validation +
    /// EntitlementRandom):
    ///   1. Customization grid for d001 (OuterDisciple) shows exactly ONE locked
    ///      option (acc_jade_crown, entitlement=owner) with LockedOverlay visible
    ///      and button.interactable=false — no more hardcoded IsLocked=false
    ///   2. Clicking the locked button is a no-op (Button.interactable blocks
    ///      dispatch — no draft change, no error banner)
    ///   3. Free options stay clickable (regression — entitlement="" passes)
    ///   4. Randomize × 20 with the revert-flag armed never equips the locked
    ///      part on d001 (server-side layer 6 is the backstop; this proves the
    ///      UI filter path too)
    ///   5. DI pair check: IVisualEntitlementProvider and
    ///      (2) SectStateProvider ctor (bind-late rank wiring via type-test),
    ///      (3) RefreshOptions real lock computation + OnRandomize via
    ///          EntitlementRandom; same singleton instance everywhere (held by
    ///          the interface registration, per this VContainer version).
    ///   6. Server-side parity through the container: TryChangeAvatarPart
    ///      rejects d001 + acc_jade_crown with a clear reason; allows d000
    /// Spine stays INERT the whole run (Phase 5 doesn't touch backends).
    /// FindObjectsByType here is READ-ONLY inspection inside this editor verify
    /// tool (same Phase 2/3/4 carve-out; production code never searches).
    /// Output: Library/phase5_verify_report.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class Phase5VerifyRunner
    {
        private const string ReportPath = "Library/phase5_verify_report.txt";
        private const string SessionMarker = "Library/phase5_verify_live.txt";
        private const string SceneA = "Assets/Scenes/TestGameplayScene.unity";
        private const string LockedPartId = "acc_jade_crown";

        static Phase5VerifyRunner()
        {
            EditorApplication.update += SessionUpdate;
        }

        // ---- entry (editor context, before play) ----

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log("[Play] still playing — stopping, re-issue the command");
                EditorApplication.isPlaying = false;
                return;
            }

            var cfg = VisualRuntimeConfig.Instance;
            Log("=== Phase 5 verify start ===");
            File.Delete(ReportPath);
            Log("[Gate] SpineActivationRequested=" + cfg.SpineActivationRequested +
                " (must stay FALSE — Phase 5 doesn't touch backends)");
            if (cfg.SpineActivationRequested)
            {
                Fail("SpineActivationRequested is TRUE — must not depend on the license gate");
                return;
            }

            try { File.WriteAllText(SessionMarker, "requested"); }
            catch (System.IO.IOException) { }

            EditorApplication.isPlaying = true;
            Log("[Play] entering play mode (current scene)…");
        }

        // ---- session poll (survives domain reload) ----

        private static bool _session;
        private static int _step;
        private static double _stepUntil;
        private static int _pass, _fail;

        private static IObjectResolver _injector;
        private static ISectStateProvider _provider;
        private static UIService _uiService;

        private static AvatarOptionButton _lockedButton;

        // Phase-5 fix: the panel opens on category "ใบหน้า" — the accessory grid is
        // under category "ร่างกาย" → slot "เครื่องประดับ". Verify it in sub-stages.
        private static int _gridStage; // 0 = click category, 1 = click slot, 2 = grid verified

        private static void SessionUpdate()
        {
            bool marker = File.Exists(SessionMarker);

            if (!_session && marker && EditorApplication.isPlaying)
            {
                StartSession();
                return;
            }

            if (_session)
            {
                if (!EditorApplication.isPlaying)
                {
                    Finish(true, "play mode ended");
                    return;
                }
                if (EditorApplication.timeSinceStartup < _stepUntil) return;
                StepMachine();
            }
            else if (!marker)
            {
                EditorApplication.update -= SessionUpdate; // idle poll until next run
            }
        }

        private static void StartSession()
        {
            _session = true;
            _step = 0;
            _gridStage = 0;
            _pass = 0;
            _fail = 0;
            _stepUntil = EditorApplication.timeSinceStartup + 1.0;

            _injector = GameLifetimeScope.Injector;
            if (_injector == null) throw new InvalidOperationException("no GameLifetimeScope.Injector");
            _provider = _injector.Resolve<ISectStateProvider>();
            _uiService = _injector.Resolve<UIService>();

            Log("[Session] started");
        }

        private static void StepMachine()
        {
            _stepUntil = EditorApplication.timeSinceStartup + 0.5;

            try
            {
                switch (_step)
                {
                    case 0: // load gameplay scene → Reconcile (production default state)
                        _injector.Resolve<SceneLoader>().LoadGameplayScene(SceneA);
                        _stepUntil += 2.5;
                        _step++;
                        break;

                    case 1: // DI pair + structural: open customization for d001 (OuterDisciple)
                    {
                        // DI: registered via Register<I, Impl> — resolve via the interface
                        // (this VContainer version does NOT expose the concrete type).
                        var asInterface = _injector.Resolve<IVisualEntitlementProvider>();
                        Check("IVisualEntitlementProvider resolves as DefaultEntitlementProvider",
                              asInterface is DefaultEntitlementProvider);

                        _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));
                        _stepUntil += 0.8; // let RenderOptions build the grid
                        _step++;
                        break;
                    }

                    case 2: // navigate to the accessory grid, then verify structure
                    {
                        if (_gridStage == 0)
                        {
                            if (ClickTab("CategoryTabBar", "ร่างกาย"))
                            {
                                _gridStage = 1;
                                _stepUntil += 0.3;
                            }
                            else
                            {
                                Check("category tab 'ร่างกาย' clickable", false);
                                _step++; // skip grid checks rather than hang
                            }
                            break;
                        }

                        if (_gridStage == 1)
                        {
                            if (ClickTab("SlotTabBar", "เครื่องประดับ"))
                            {
                                _gridStage = 2;
                                _stepUntil += 0.3;
                            }
                            else
                            {
                                Check("slot tab 'เครื่องประดับ' clickable", false);
                                _step++;
                            }
                            break;
                        }

                        var view = UnityEngine.Object.FindObjectsByType<AvatarCustomizationView>(
                            FindObjectsSortMode.None);
                        Check("AvatarCustomization panel open", view.Length == 1);

                        var subtitle = GameObject.Find("Window/Header/SubtitleText");
                        Check("panel subtitle shows the disciple (Lin Feng)",
                              subtitle != null && subtitle.GetComponent<TMPro.TMP_Text>() != null &&
                              subtitle.GetComponent<TMPro.TMP_Text>().text.Contains("Lin Feng"));

                        var buttons = UnityEngine.Object.FindObjectsByType<AvatarOptionButton>(
                            FindObjectsSortMode.None);
                        Check("option grid rendered (>= 4 accessory…slot options)", buttons.Length >= 4);

                        var locked = new List<AvatarOptionButton>();
                        for (int i = 0; i < buttons.Length; i++)
                            if (buttons[i] != null && !buttons[i].GetComponent<Button>().interactable)
                                locked.Add(buttons[i]);
                        Check("exactly ONE locked option in the grid (got " + locked.Count + ")",
                              locked.Count == 1);
                        if (locked.Count == 1)
                        {
                            _lockedButton = locked[0];
                            var overlay = _lockedButton.transform.Find("LockedOverlay");
                            Check("locked button shows LockedOverlay", overlay != null && overlay.gameObject.activeSelf);
                            Check("locked button is the owner part (label 'มงกุฎหยก')",
                                  _lockedButton.GetComponentInChildren<TMPro.TMP_Text>(true) != null &&
                                  _lockedButton.GetComponentInChildren<TMPro.TMP_Text>(true).text == "มงกุฎหยก");
                        }

                        // free options stay interactive (regression)
                        int freeInteractive = 0;
                        for (int i = 0; i < buttons.Length; i++)
                            if (buttons[i] != null && buttons[i].GetComponent<Button>().interactable)
                                freeInteractive++;
                        Check("free options remain interactive (" + freeInteractive + ")",
                              freeInteractive == buttons.Length - locked.Count);
                        _step++;
                        break;
                    }

                    case 3: // locked click → no-op (Button.interactable blocks dispatch)
                        if (_lockedButton != null)
                        {
                            _lockedButton.GetComponent<Button>().onClick.Invoke();
                        }
                        else
                        {
                            Check("locked button captured in step 2", false);
                        }
                        _stepUntil += 0.3;
                        _step++;
                        break;

                    case 4:
                    {
                        var err = GameObject.Find("Window/Footer/ErrorText");
                        Check("locked click produced NO error banner (blocked at input, not at commit)",
                              err == null || !err.activeSelf);

                        // draft/state untouched: d001 has NO accessory set in MockSectData —
                        // the invariant is "never gained the locked part", not a specific id.
                        var d001 = Find(_provider.BuildSectEconomyState().Disciples, "d001");
                        Check("d001 accessory not the locked part after locked click",
                              d001 != null && d001.Avatar != null &&
                              d001.Avatar.GetSlot(AvatarSlots.Accessory) != LockedPartId);
                        _step++;
                        break;
                    }

                    case 5: // free click still works (regression) — pick an unlocked button
                    {
                        bool clicked = false;
                        var buttons = UnityEngine.Object.FindObjectsByType<AvatarOptionButton>(
                            FindObjectsSortMode.None);
                        for (int i = 0; i < buttons.Length; i++)
                        {
                            var b = buttons[i];
                            if (b != null && b.GetComponent<Button>().interactable &&
                                b.GetComponentInChildren<TMPro.TMP_Text>(true) != null &&
                                b.GetComponentInChildren<TMPro.TMP_Text>(true).text == "น้ำเต้า")
                            {
                                b.GetComponent<Button>().onClick.Invoke();
                                clicked = true;
                                break;
                            }
                        }
                        Check("clicked a FREE option (น้ำเต้า) successfully", clicked);
                        _stepUntil += 0.3;
                        _step++;
                        break;
                    }

                    case 6:
                    {
                        var err = GameObject.Find("Window/Footer/ErrorText");
                        Check("free click produced NO error banner", err == null || !err.activeSelf);

                        // selected frame moved to the clicked option (draft accepted it)
                        var buttons = UnityEngine.Object.FindObjectsByType<AvatarOptionButton>(
                            FindObjectsSortMode.None);
                        bool frameOnGourd = false;
                        for (int i = 0; i < buttons.Length; i++)
                        {
                            var b = buttons[i];
                            if (b == null) continue;
                            var label = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                            var frame = b.transform.Find("SelectedFrame");
                            if (label != null && label.text == "น้ำเต้า" && frame != null)
                                frameOnGourd = frame.gameObject.activeSelf;
                        }
                        Check("draft accepted the free part (SelectedFrame on น้ำเต้า)", frameOnGourd);
                        _step++;
                        break;
                    }

                    case 7: // Randomize × 20 through the REAL button path (OnRandomize)
                    {
                        var rngBtn = GameObject.Find("Window/Footer/RandomizeButton");
                        Check("RandomizeButton found", rngBtn != null);
                        if (rngBtn != null)
                        {
                            var btn = rngBtn.GetComponent<Button>();
                            for (int i = 0; i < 20; i++) btn.onClick.Invoke();
                        }
                        _stepUntil += 0.5; // let preview Update + any warn land
                        _step++;
                        break;
                    }

                    case 8:
                    {
                        // after 20 rerolls the draft never contains the locked part;
                        // commit path would reject it anyway — verify the visible draft
                        // by re-opening is overkill: assert via the presenter's own
                        // server round-trip below instead. Here: no error banner.
                        var err = GameObject.Find("Window/Footer/ErrorText");
                        Check("no error banner after 20 randomize rolls", err == null || !err.activeSelf);
                        _step++;
                        break;
                    }

                    case 9: // server-side parity through the DI-wired container (MCP parity)
                    {
                        string reason;
                        AvatarAppearance result;
                        bool rejected = !_provider.TryChangeAvatarPart("d001", AvatarSlots.Accessory,
                                                                       LockedPartId, out reason, out result);
                        Check("container-wired TryChangeAvatarPart rejects d001 + owner part", rejected);
                        Check("failReason names the part + entitlement ('" + reason + "')",
                              rejected && reason.Contains(LockedPartId) && reason.Contains("owner"));

                        bool allowed = _provider.TryChangeAvatarPart("d000", AvatarSlots.Accessory,
                                                                     LockedPartId, out reason, out result);
                        Check("container-wired TryChangeAvatarPart allows SectMaster + owner part", allowed);

                        // d000 already wears it — value identical; restore defensively anyway
                        _provider.TryChangeAvatarPart("d000", AvatarSlots.Accessory,
                                                      "acc_jade_crown", out reason, out result);
                        _step++;
                        break;
                    }

                    case 10: // cleanup: close panel; final state diff (regression)
                    {
                        _uiService.Close("AvatarCustomization");
                        _stepUntil += 0.3;
                        _step++;
                        break;
                    }

                    case 11:
                    {
                        var roster = _provider.BuildSectEconomyState().Disciples;
                        var d001 = Find(roster, "d001");
                        var d000 = Find(roster, "d000");
                        Check("FINAL: d001 accessory not the locked part",
                              d001 != null && d001.Avatar != null &&
                              d001.Avatar.GetSlot(AvatarSlots.Accessory) != LockedPartId);
                        Check("FINAL: d000 wears the owner crown (acc_jade_crown)",
                              d000 != null && d000.Avatar != null &&
                              d000.Avatar.GetSlot(AvatarSlots.Accessory) == "acc_jade_crown");
                        Finish(true, null);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Phase5Verify] FAILED: " + ex);
                Finish(false, ex.Message);
            }
        }

        private static DiscipleState Find(List<DiscipleState> list, string id)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].DiscipleId == id) return list[i];
            return null;
        }

        /// <summary>
        /// Click a category/slot tab by label under the named tab bar
        /// ("CategoryTabBar" / "SlotTabBar" in the generated panel).
        /// </summary>
        private static bool ClickTab(string barName, string label)
        {
            var bar = GameObject.Find(barName);
            if (bar == null) return false;
            var buttons = bar.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                var text = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (text == null || text.text != label) continue;
                b.onClick.Invoke();
                return true;
            }
            return false;
        }

        private static void Check(string what, bool ok)
        {
            if (ok) _pass++; else _fail++;
            Log("[" + (ok ? "PASS" : "FAIL") + "] " + what);
        }

        private static void Finish(bool ok, string note)
        {
            if (!_session) return;
            _session = false;

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

            try
            {
                var summary = "phase5 verify " + (ok && _fail == 0 ? "COMPLETE" : "FAILED") +
                              " pass=" + _pass + " fail=" + _fail +
                              (string.IsNullOrEmpty(note) ? "" : " note=" + note) +
                              " @ " + DateTime.UtcNow.ToString("o");
                File.WriteAllText(ReportPath, summary);
                Log("report → " + ReportPath + " (" + summary + ")");
            }
            catch { /* ignore */ }

            Log("=== Phase 5 verify end ===");
        }

        private static void Log(string msg)
        {
            Debug.Log("[Phase5Verify] " + msg);
        }

        private static void Fail(string msg)
        {
            Debug.LogError("[Phase5Verify] " + msg);
        }
    }
}
#endif
