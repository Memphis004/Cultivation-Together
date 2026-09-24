using System;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    public class WalletHudPresenter : UIPresenter<WalletHudView>
    {
        private readonly ISubscriber<SectResourceChangedMessage> _resourceSubscriber;
        private readonly ISectStateProvider _stateProvider;

        private IDisposable _subscription;

        public WalletHudPresenter(
            ISubscriber<SectResourceChangedMessage> resourceSubscriber,
            ISectStateProvider stateProvider)
        {
            _resourceSubscriber = resourceSubscriber;
            _stateProvider = stateProvider;
        }

        protected override void OnViewBound()
        {
            _subscription = _resourceSubscriber.Subscribe(OnResourceChanged);

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

        public override void Dispose()
        {
            _subscription?.Dispose();
        }
    }
}
