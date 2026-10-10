using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P10C — the DiscipleDetail "ข้อมูล" tab: stamina (bar + number) and the three skill
    /// levels, over the REAL view + presenter + SectStateProvider (mock start state).
    ///
    /// Covered here:
    ///   - the tab binds stamina/level/XP from the shared P10A helpers on open;
    ///   - the live refresh runs at most ~4×/s on unscaled time;
    ///   - it stops when the panel is hidden and after Dispose, and it never touches the
    ///     สเตตัส (radar) tab;
    ///   - low stamina is a VISUAL indicator only — the panel reassigns nobody.
    ///
    /// No reflection and no FindObjectOfType: refs are wired through SerializedObject, the
    /// same mechanism the panel generator uses (pattern from DiscipleListPresenterTests).
    /// </summary>
    public class DiscipleDetailAttributeTests
    {
        private SectStateProvider _provider;
        private DiscipleDetailPresenter _presenter;
        private DiscipleDetailView _view;
        private GameObject _rootGo;
        private RectTransform _staminaBar;
        private Image _staminaFill;
        private TMP_Text _staminaText, _skillGatheringText, _skillAlchemyText, _skillForgingText;
        private readonly List<Button> _tabButtons = new List<Button>();

        // Kept so the tests can prove an attribute tick publishes NO game message.
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;
        private BufferPublisher<SectResourceChangedMessage> _resourceChanged;

        [SetUp]
        public void SetUp()
        {
            // The fixture instance is reused across test methods, so the tab buttons must be
            // dropped explicitly — a stale button from a previous test would swallow the click.
            _tabButtons.Clear();

            _taskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();
            _resourceChanged = new BufferPublisher<SectResourceChangedMessage>();

            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                _resourceChanged,
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                _taskChanged);

            _presenter = new DiscipleDetailPresenter(_provider, new AvatarPartPool());

            // ── minimal real view hierarchy (rail + tabs + content host + the P10C texts) ──
            _rootGo = new GameObject("DiscipleDetailTestRoot", typeof(RectTransform));
            _rootGo.SetActive(false); // defer Awake until the refs are wired

            _view = _rootGo.AddComponent<DiscipleDetailView>();

            var rail = InkWidgets.Rect(_rootGo.transform, "Rail", 0, 0, 720, 64);

            var tabs = InkWidgets.Rect(_rootGo.transform, "Tabs", 0, 0, 1230, 52);
            for (int i = 0; i < 6; i++)
            {
                var tab = InkWidgets.Rect(tabs, "Tab_" + i, i * 202, 0, 190, 52);
                var image = InkWidgets.Fill(tab, UiPalette.Paper, true);
                var button = tab.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                _tabButtons.Add(button);
            }

            var host = InkWidgets.Rect(_rootGo.transform, "ContentHost", 0, 0, 924, 522);
            var info = InkWidgets.Rect(host, "InfoContent", 0, 0, 924, 522);
            for (int i = 1; i < 6; i++) InkWidgets.Rect(host, "Tab" + i, 0, 0, 924, 522);

            _staminaBar = InkWidgets.Rect(info, "StaminaBar", 28, 33, 300, 9);
            InkWidgets.Fill(_staminaBar, UiPalette.Disabled);
            _staminaFill = InkWidgets.Fill(InkWidgets.Rect(_staminaBar, "Fill", 0, 0, 300, 9), UiPalette.Jade);
            _staminaText = InkWidgets.Text(info, "StaminaValue", "", null, 26, 536, 0, 378, 48,
                                           UiPalette.Text, TextAlignmentOptions.MidlineRight);
            _skillGatheringText = InkWidgets.Text(info, "SkillRowGathering", "", null, 26, 28, 310, 500, 48);
            _skillAlchemyText = InkWidgets.Text(info, "SkillRowAlchemy", "", null, 26, 28, 372, 500, 48);
            _skillForgingText = InkWidgets.Text(info, "SkillRowForging", "", null, 26, 28, 434, 500, 48);

            var closeGo = new GameObject("CloseButton", typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_rootGo.transform, false);

            var so = new SerializedObject(_view);
            so.FindProperty("railContent").objectReferenceValue = rail;
            so.FindProperty("tabRail").objectReferenceValue = tabs;
            so.FindProperty("contentHost").objectReferenceValue = host;
            so.FindProperty("closeButton").objectReferenceValue = closeGo.GetComponent<Button>();
            so.FindProperty("staminaBarFill").objectReferenceValue = _staminaFill;
            so.FindProperty("staminaText").objectReferenceValue = _staminaText;
            so.FindProperty("skillGatheringText").objectReferenceValue = _skillGatheringText;
            so.FindProperty("skillAlchemyText").objectReferenceValue = _skillAlchemyText;
            so.FindProperty("skillForgingText").objectReferenceValue = _skillForgingText;
            so.ApplyModifiedPropertiesWithoutUndo();

            _rootGo.SetActive(true);
            _view.Show(); // production path: UIService.Open calls Show() before the presenter opens
        }

        [TearDown]
        public void TearDown()
        {
            // Production close path: the panel presenter dies with the panel, so the
            // subscription to the view's refresh tick never outlives it.
            _presenter?.Dispose();
            if (_rootGo != null) Object.DestroyImmediate(_rootGo);
        }

        // ---- helpers ----

        private void Open(string discipleId)
        {
            _presenter.Bind(_view);
            _presenter.OnOpen(discipleId);
        }

        private DiscipleState Find(string id)
        {
            var disciples = _provider.BuildSectEconomyState().Disciples;
            for (int i = 0; i < disciples.Count; i++)
                if (disciples[i] != null && disciples[i].DiscipleId == id) return disciples[i];
            return null;
        }

        private void ClickTab(DiscipleTab tab) => _tabButtons[(int)tab].onClick.Invoke();

        // ---- the tab binds stamina + skills ----

        [Test]
        public void InfoTab_ShowsStaminaBarNumberAndTheThreeSkillLevels()
        {
            var d = Find("d001");
            d.Attributes.Stamina = 62f;
            d.Attributes.SkillXp[DiscipleAttributesConfig.CategoryGathering] = 340f;   // Lv 3
            d.Attributes.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = DiscipleAttributesConfig.SkillXpMax;

            Open("d001");

            Assert.AreEqual("62 / 100", _staminaText.text, "stamina is shown as a number");
            Assert.AreEqual(0.62f, _staminaFill.rectTransform.sizeDelta.x / _staminaBar.rect.width, 0.001f,
                            "the bar fill is proportional to stamina");

            Assert.AreEqual("Lv 3 · XP 340", _skillGatheringText.text);
            Assert.AreEqual("Lv 10 · XP 1000", _skillAlchemyText.text);
            Assert.AreEqual("Lv 0 · XP 0", _skillForgingText.text);
        }

        // ---- refresh cadence: ≤ ~4 per second, unscaled, open only ----

        [Test]
        public void Refresh_IsThrottledToAboutFourPerSecond()
        {
            Open("d001");

            Assert.IsTrue(_view.TickAttributeRefresh(10f), "the first tick refreshes");
            Assert.IsFalse(_view.TickAttributeRefresh(10.1f), "inside the 0.25s window nothing happens");
            Assert.IsTrue(_view.TickAttributeRefresh(10f + DiscipleDetailView.AttributeRefreshIntervalSeconds),
                          "the next window refreshes again");
        }

        [Test]
        public void Refresh_UpdatesTheOpenInfoTab_AndStopsWhenClosed()
        {
            Find("d001").Attributes.Stamina = 50f;
            Open("d001");
            _view.TickAttributeRefresh(1f);
            Assert.AreEqual("50 / 100", _staminaText.text);

            // open: a real attribute change lands on the next tick
            Find("d001").Attributes.Stamina = 70f;
            _view.TickAttributeRefresh(2f);
            Assert.AreEqual("70 / 100", _staminaText.text, "the open panel refreshes live");

            // closed (Hide() is the panel's close path): no tick at all
            _view.Hide();
            Find("d001").Attributes.Stamina = 90f;
            Assert.IsFalse(_view.TickAttributeRefresh(3f), "a hidden panel does not refresh");
            Assert.AreEqual("70 / 100", _staminaText.text, "a closed panel keeps its last values");

            // reopened: it refreshes again
            _view.Show();
            Assert.IsTrue(_view.TickAttributeRefresh(4f));
            Assert.AreEqual("90 / 100", _staminaText.text);

            // every tick above only wrote text: not one message was published
            Assert.AreEqual(0, _taskChanged.Messages.Count, "no task-change message per attribute tick");
            Assert.AreEqual(0, _resourceChanged.Messages.Count, "no resource message per attribute tick");
        }

        [Test]
        public void Refresh_IsSkippedWhileAnotherTabIsSelected()
        {
            Find("d001").Attributes.Stamina = 50f;
            Open("d001");
            _view.TickAttributeRefresh(1f);
            Assert.AreEqual("50 / 100", _staminaText.text);

            ClickTab(DiscipleTab.Status); // สเตตัส stays the untouched radar placeholder
            Assert.IsTrue(_view.GetTabContent(DiscipleTab.Status).gameObject.activeSelf,
                          "the click switches the visible tab");
            Find("d001").Attributes.Stamina = 33f;
            _view.TickAttributeRefresh(2f);
            Assert.AreEqual("50 / 100", _staminaText.text,
                            "the สเตตัส tab refresh writes no attribute text");

            ClickTab(DiscipleTab.Info);
            Find("d001").Attributes.Stamina = 21f;
            _view.TickAttributeRefresh(3f);
            Assert.AreEqual("21 / 100", _staminaText.text, "the open ข้อมูล tab refreshes live again");
        }

        [Test]
        public void Dispose_StopsTheRefresh()
        {
            Find("d001").Attributes.Stamina = 50f;
            Open("d001");
            _view.TickAttributeRefresh(1f);
            Assert.AreEqual("50 / 100", _staminaText.text);

            _presenter.Dispose();
            Find("d001").Attributes.Stamina = 5f;
            _view.TickAttributeRefresh(2f);

            Assert.AreEqual("50 / 100", _staminaText.text, "no refresh after Dispose (no leaked subscription)");
        }

        // ---- low stamina: visual only ----

        [Test]
        public void LowStamina_ShowsTheIndicator_AndReassignsNobody()
        {
            Open("d001");

            Find("d001").Attributes.Stamina = DiscipleAttributesConfig.RecoveryThresholdLow;
            _view.TickAttributeRefresh(1f);

            Assert.AreEqual(UiPalette.Danger, _staminaFill.color, "low stamina is flagged in the palette");
            Assert.AreEqual("25 / 100", _staminaText.text);
            Assert.AreEqual("meditation", Find("d001").CurrentTask,
                            "the panel only displays — it never reassigns a disciple");

            Find("d001").Attributes.Stamina = DiscipleAttributesConfig.RecoveryThresholdHigh;
            _view.TickAttributeRefresh(2f);
            Assert.AreEqual(UiPalette.Jade, _staminaFill.color, "a recovered bar is back to the healthy colour");
        }

        // ---- plumbing ----

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
