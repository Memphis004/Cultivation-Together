using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// §5 + §6 — DiscipleList presenter flow (ผ่าน view จริง + UGUI hierarchy เล็ก ๆ):
    /// 1. OnOpen สร้างการ์ดครบทุกศิษย์ (จาก ISectStateProvider)
    /// 2. คลิกการ์ด publish DiscipleSelectedMessage ครั้งเดียว (id ถูกต้อง)
    /// 3. CloseCallback จาก DiscipleListArgs ถูกเรียกเมื่อกดปุ่มปิดในตัว panel
    /// 4. Dispose แล้วการ์ดไม่ publish อีก (unsubscribe rule)
    ///
    /// ไม่ใช้ FindObjectOfType / reflection — wire refs ผ่าน SerializedObject
    /// (กลไกเดียวกับ generator ของโปรเจกต์) และ Awake ถูกดีเลย์ด้วย SetActive(false)
    /// เพื่อให้ refs ครบก่อน lifecycle จริง
    /// </summary>
    public class DiscipleListPresenterTests
    {
        private FakePublisher _publisher;
        private DiscipleListPresenter _presenter;
        private DiscipleListView _view;
        private Button _closeButton;
        private GameObject _rootGo;

        [SetUp]
        public void SetUp()
        {
            _publisher = new FakePublisher();
            _presenter = new DiscipleListPresenter(_publisher, new FakeStateProvider());

            // ── minimal real view hierarchy (cards + close button) ──
            _rootGo = new GameObject("DiscipleListTestRoot", typeof(RectTransform));
            _rootGo.SetActive(false); // defer Awake จน wire refs เสร็จ
            _view = _rootGo.AddComponent<DiscipleListView>();

            var cardsGo = new GameObject("CardsRoot", typeof(RectTransform));
            cardsGo.transform.SetParent(_rootGo.transform, false);

            var closeGo = new GameObject("CloseButton", typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_rootGo.transform, false);
            _closeButton = closeGo.GetComponent<Button>();

            var so = new SerializedObject(_view);
            so.FindProperty("cardsRoot").objectReferenceValue = (RectTransform)cardsGo.transform;
            so.FindProperty("closeButton").objectReferenceValue = _closeButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            _rootGo.SetActive(true);
            _view.Show(); // production path: UIService.Open เรียก Show() เสมอ — wire close listener ตรงนี้
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null) Object.DestroyImmediate(_rootGo);
        }

        private List<DiscipleListCard> Cards()
        {
            var cards = new List<DiscipleListCard>();
            foreach (Transform child in _view.CardsRoot)
            {
                var card = child.GetComponent<DiscipleListCard>();
                if (card != null) cards.Add(card);
            }
            return cards;
        }

        [Test]
        public void OnOpen_BuildsOneCardPerDisciple()
        {
            _presenter.Bind(_view);
            _presenter.OnOpen(new DiscipleListArgs());

            var cards = Cards();
            Assert.AreEqual(3, cards.Count, "one card per mock disciple");
            Assert.AreEqual("d001", cards[0].DiscipleId);
        }

        [Test]
        public void OnCardClicked_PublishesDiscipleSelected_Once_WithCorrectId()
        {
            _presenter.Bind(_view);
            _presenter.OnOpen(new DiscipleListArgs());

            var cards = Cards();
            cards[1].Click();

            Assert.AreEqual(1, _publisher.Published.Count, "card click must publish exactly once");
            Assert.AreEqual("d002", _publisher.Published[0].DiscipleId);

            cards[0].Click();
            Assert.AreEqual(2, _publisher.Published.Count);
            Assert.AreEqual("d001", _publisher.Published[1].DiscipleId);
        }

        [Test]
        public void OnOpen_WithoutArgs_DoesNotThrow_AndStillBuildsCards()
        {
            _presenter.Bind(_view);

            Assert.DoesNotThrow(() => _presenter.OnOpen(null));
            Assert.AreEqual(3, Cards().Count);
        }

        [Test]
        public void CloseButton_InvokesCloseCallback_FromArgs()
        {
            _presenter.Bind(_view);

            var closeCalled = 0;
            _presenter.OnOpen(new DiscipleListArgs { CloseCallback = () => closeCalled++ });

            _closeButton.onClick.Invoke(); // ปุ่มปิดในตัว panel (listener ถูก wire ใน Awake จริง)
            Assert.AreEqual(1, closeCalled, "panel's own close button must close through the opener's callback");
        }

        [Test]
        public void Dispose_UnsubscribesCardClicks()
        {
            _presenter.Bind(_view);
            _presenter.OnOpen(new DiscipleListArgs());

            _presenter.Dispose();
            _publisher.Published.Clear();

            Cards()[0].Click();
            Assert.AreEqual(0, _publisher.Published.Count, "no publish after Dispose (memory-leak rule)");
        }

        // ── fakes ──

        private class FakeStateProvider : ISectStateProvider
        {
            public SectEconomyState BuildSectEconomyState()
            {
                var state = new SectEconomyState();
                state.Disciples.Add(new DiscipleState { DiscipleId = "d001", DisplayName = "Lin Feng" });
                state.Disciples.Add(new DiscipleState { DiscipleId = "d002", DisplayName = "Su Yan" });
                state.Disciples.Add(new DiscipleState { DiscipleId = "d003", DisplayName = "Elder Zhao" });
                return state;
            }

            public void ApplyDecisionConsequence(string eventId, string choiceId) { }
            public void TickGathering(float deltaTimeSeconds) { }
            public void TickCrafting(float deltaTimeSeconds) { }
            public void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified) { }
            public PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity)
                => new PurchaseItemResponse();
            public bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                                            out string failReason, out AvatarAppearance result)
            {
                failReason = null; result = null; return false;
            }
            public bool TrySetChibiBackend(string discipleId, ChibiBackend backend, out string failReason)
            {
                failReason = null; return false;
            }
        }

        private class FakePublisher : IPublisher<DiscipleSelectedMessage>
        {
            public readonly List<DiscipleSelectedMessage> Published = new List<DiscipleSelectedMessage>();
            public void Publish(DiscipleSelectedMessage message) => Published.Add(message);
        }
    }
}
