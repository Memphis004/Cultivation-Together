using System;
using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>Open args for the resource popup - carries the close callback
    /// provided by the opener (UIBootstrap) so the popup never needs a
    /// UIService reference of its own. Passed via UIService.Open(id, args).</summary>
    public sealed class ResourcePopupArgs
    {
        public System.Action CloseCallback;
    }

    // MVP Lite presenter for the resource popup (คลังสินค้า). Read-only view
    // of SectStockpile.RawResources - NO data-layer changes: values come from
    // ISectStateProvider on open, then live updates arrive via
    // SectResourceChangedMessage while the popup is open.
    //
    // The reference art shows 7 rows; the economy only tracks 4 raw
    // resources (herb/wood/ore/provisions). The remaining 3 are rendered as
    // disabled, greyed-out rows showing "—" (design decision recorded in
    // the task summary) so the layout matches without inventing data.
    public class ResourcePopupPresenter : UIPresenter<ResourcePopupView>
    {
        private const string PlaceholderValue = "—";

        private readonly ISubscriber<SectResourceChangedMessage> _resourceSubscriber;
        private readonly ISectStateProvider _stateProvider;

        private IDisposable _subscription;
        private System.Action _closeCallback;
        private readonly Dictionary<string, ResourcePopupRow> _rowsByResourceId =
            new Dictionary<string, ResourcePopupRow>();

        public ResourcePopupPresenter(
            ISubscriber<SectResourceChangedMessage> resourceSubscriber,
            ISectStateProvider stateProvider)
        {
            _resourceSubscriber = resourceSubscriber;
            _stateProvider = stateProvider;
        }

        protected override void OnViewBound()
        {
            View.CloseClicked += OnCloseClicked;

            BuildRows();

            // UIService.Open on an already-open panel calls OnOpen again -
            // replace (not stack) the live-update subscription.
            _subscription?.Dispose();
            _subscription = _resourceSubscriber.Subscribe(OnResourceChanged);
        }

        public override void OnOpen(object args)
        {
            _closeCallback = (args as ResourcePopupArgs)?.CloseCallback;

            // Re-prime on every open so values are fresh even if the popup
            // was kept alive by UIService's dedupe.
            PrimeValues();
        }

        private void BuildRows()
        {
            View.ClearRows();
            _rowsByResourceId.Clear();

            // Real, tracked resources first (enabled rows), primed with
            // current values.
            foreach (var kv in _stateProvider.BuildSectEconomyState().Stockpile.RawResources)
            {
                var row = View.CreateRow();
                if (row == null) return;

                row.Set(GetLabel(kv.Key), kv.Value.ToString(), enabled: true);
                _rowsByResourceId[kv.Key] = row;
            }

            // Reference-only resources the economy does not track yet:
            // disabled/greyed-out rows so the layout matches the reference
            // without inventing stockpile data.
            AddPlaceholderRow("เหล็กนิล");
            AddPlaceholderRow("กระดาษยันต์");
            AddPlaceholderRow("หินคายกล");
        }

        private void AddPlaceholderRow(string label)
        {
            var row = View.CreateRow();
            if (row == null) return;

            row.Set(label, PlaceholderValue, enabled: false);
        }

        private void PrimeValues()
        {
            foreach (var kv in _stateProvider.BuildSectEconomyState().Stockpile.RawResources)
            {
                if (_rowsByResourceId.TryGetValue(kv.Key, out var row) && row != null)
                    row.SetValue(kv.Value.ToString());
            }
        }

        private void OnResourceChanged(SectResourceChangedMessage message)
        {
            if (_rowsByResourceId.TryGetValue(message.ResourceId, out var row) && row != null)
                row.SetValue(message.NewTotal.ToString());
        }

        private static string GetLabel(string resourceId) => resourceId switch
        {
            "herb" => "สมุนไพร",
            "wood" => "ไม้",
            "ore" => "แร่",
            "provisions" => "เสบียง",
            _ => resourceId,
        };

        private void OnCloseClicked() => _closeCallback?.Invoke();

        public override void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;

            if (View != null)
                View.CloseClicked -= OnCloseClicked;

            _rowsByResourceId.Clear();
            _closeCallback = null;
        }
    }
}
