using System;
using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>Open args for the disciple list — carries the close callback
    /// provided by the opener (UIBootstrap.WireDiscipleList) so the panel never
    /// needs a UIService reference of its own. Same shape as ResourcePopupArgs,
    /// passed via UIService.Open(id, args).</summary>
    public sealed class DiscipleListArgs
    {
        public System.Action CloseCallback;
    }

    // MVP Lite presenter for the disciple list ("ศิษย์" panel). Read-only view
    // of the roster from ISectStateProvider — NO data-layer changes.
    //
    // Cards show the baked square icon (AvatarIconBaker output, plan §1) +
    // name/rank/task/wallet. Clicking a card publishes DiscipleSelectedMessage
    // (in-memory only), the SAME message ChibiClickTarget publishes — so the
    // existing DiscipleDetailUISystem opens DiscipleDetail with zero extra
    // wiring (one code path for both entry points).
    public class DiscipleListPresenter : UIPresenter<DiscipleListView>
    {
        private readonly IPublisher<DiscipleSelectedMessage> _discipleSelectedPublisher;
        private readonly ISectStateProvider _stateProvider;

        private System.Action _closeCallback;
        private readonly List<DiscipleListCard> _cards = new List<DiscipleListCard>();

        public DiscipleListPresenter(
            IPublisher<DiscipleSelectedMessage> discipleSelectedPublisher,
            ISectStateProvider stateProvider)
        {
            _discipleSelectedPublisher = discipleSelectedPublisher;
            _stateProvider = stateProvider;
        }

        protected override void OnViewBound()
        {
            View.CloseClicked += OnCloseClicked;
        }

        public override void OnOpen(object args)
        {
            _closeCallback = (args as DiscipleListArgs)?.CloseCallback;

            // Rebuild on every open so the roster is fresh even if the panel
            // was kept alive by UIService's dedupe (ResourcePopup re-primes too).
            BuildCards();
        }

        private void BuildCards()
        {
            View.ClearCards();
            _cards.Clear();

            var disciples = _stateProvider.BuildSectEconomyState()?.Disciples;
            if (disciples == null) return;

            foreach (var d in disciples)
            {
                if (d == null) continue;

                var card = View.CreateCard();
                if (card == null) return;

                card.Clicked += OnCardClicked;
                card.Set(
                    d.DiscipleId,
                    d.DisplayName,
                    d.Rank + (string.IsNullOrEmpty(d.CurrentTask) ? "" : " · " + d.CurrentTask),
                    (d.Wallet?.SpiritStones ?? 0).ToString(),
                    LoadIcon(d.DiscipleId));

                _cards.Add(card);
            }
        }

        private static Sprite LoadIcon(string discipleId)
        {
            // Bake ผ่าน AvatarIconBaker (Editor) → real PNG ใต้ Resources — C5/C2
            return Resources.Load<Sprite>("Avatar/Icons/icon_" + discipleId);
        }

        private void OnCardClicked(string discipleId)
        {
            // In-memory only (same rule as every DiscipleVisualSystem message):
            // one publish → DiscipleDetailUISystem opens the detail panel.
            _discipleSelectedPublisher?.Publish(new DiscipleSelectedMessage { DiscipleId = discipleId });
        }

        private void OnCloseClicked() => _closeCallback?.Invoke();

        public override void Dispose()
        {
            if (View != null)
                View.CloseClicked -= OnCloseClicked;

            foreach (var card in _cards)
            {
                if (card != null) card.Clicked -= OnCardClicked;
            }
            _cards.Clear();
            _closeCallback = null;
        }
    }
}
