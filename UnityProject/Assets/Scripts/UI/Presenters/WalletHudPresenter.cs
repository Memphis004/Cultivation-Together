using System;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    public class WalletHudPresenter : UIPresenter<WalletHudView>
    {
        private readonly ISubscriber<SectResourceChangedMessage> _resourceSubscriber;
        private readonly ISubscriber<SessionRestoredMessage> _sessionRestoredSubscriber;
        private readonly ISectStateProvider _stateProvider;

        private IDisposable _subscription;
        private IDisposable _sessionRestoredSubscription;

        public WalletHudPresenter(
            ISubscriber<SectResourceChangedMessage> resourceSubscriber,
            ISectStateProvider stateProvider,
            ISubscriber<SessionRestoredMessage> sessionRestoredSubscriber = null)
        {
            _resourceSubscriber = resourceSubscriber;
            _stateProvider = stateProvider;
            _sessionRestoredSubscriber = sessionRestoredSubscriber;
        }

        protected override void OnViewBound()
        {
            _subscription = _resourceSubscriber.Subscribe(OnResourceChanged);
            // P11A — re-prime the persistent HUD from the restored session instead of
            // leaving the previous session's numbers on screen.
            _sessionRestoredSubscription = _sessionRestoredSubscriber?.Subscribe(OnSessionRestored);

            // Prime the view with current values immediately instead of
            // waiting for the next change - matches the old SectHudView's
            // behavior of showing correct numbers on the very first frame.
            var state = _stateProvider.BuildSectEconomyState();
            foreach (var kv in state.Stockpile.RawResources)
            {
                View.SetInitialResource(kv.Key, kv.Value);
            }

            RefreshWallet();
        }

        private void OnResourceChanged(SectResourceChangedMessage message)
        {
            View.UpdateResource(message.ResourceId, message.NewTotal, message.Delta);

            // No dedicated wallet-changed message exists yet, so piggyback
            // wallet refresh on resource-change events (which fire often
            // enough from gathering/crafting ticks) instead of adding a
            // separate polling timer to what's meant to be a plain C#
            // presenter with no tick/Update of its own.
            RefreshWallet();
        }

        private void RefreshWallet()
        {
            var state = _stateProvider.BuildSectEconomyState();
            var sectMaster = state.Disciples.Find(d => d.Rank == DiscipleRank.SectMaster);
            if (sectMaster == null)
            {
                Debug.LogWarning("[WalletHudPresenter] No disciple with Rank.SectMaster found - wallet left unset.");
                return;
            }

            View.UpdateWallet(sectMaster.Wallet.SpiritStones, sectMaster.Wallet.Contribution);
        }

        /// <summary>P11A — a full session was restored: drop the previous session's values and re-read state.</summary>
        private void OnSessionRestored(SessionRestoredMessage message)
        {
            var state = _stateProvider.BuildSectEconomyState();
            if (state == null || state.Stockpile == null) return;

            foreach (var kv in state.Stockpile.RawResources)
                View.SetInitialResource(kv.Key, kv.Value);

            RefreshWallet();
        }

        public override void Dispose()
        {
            _subscription?.Dispose();
            _sessionRestoredSubscription?.Dispose();
        }
    }
}
