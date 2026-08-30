using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Xianxia.Sect.UI
{
    // Same icon+value+delta pattern as the old SectHudView (now removed),
    // just driven by SectResourceChangedMessage via ResourceHudPresenter
    // instead of polling ISectStateProvider every N seconds.
    [Serializable]
    public class ResourceSlotBinding
    {
        [Tooltip("Must match a key in SectEconomyState.Stockpile.RawResources, e.g. \"herb\"")]
        public string resourceId;
        public TMP_Text valueText;
        public TMP_Text deltaText;
    }

    public class ResourceHudView : UIViewBase
    {
        [Header("Stockpile (with delta indicators)")]
        [SerializeField]
        private List<ResourceSlotBinding> resourceSlots = new List<ResourceSlotBinding>
        {
            new ResourceSlotBinding { resourceId = "herb" },
            new ResourceSlotBinding { resourceId = "wood" },
            new ResourceSlotBinding { resourceId = "ore" },
            new ResourceSlotBinding { resourceId = "provisions" },
        };

        [Header("Personal Wallet (Sect Master)")]
        [SerializeField] private TMP_Text spiritStonesText;
        [SerializeField] private TMP_Text contributionText;

        [Header("Delta colors")]
        [SerializeField] private Color deltaPositiveColor = new Color(0.45f, 0.85f, 0.45f);
        [SerializeField] private Color deltaNegativeColor = new Color(0.9f, 0.35f, 0.3f);

        [Header("Delta display duration")]
        [Tooltip("How long the +N/-N delta text stays visible before clearing itself. Swap the coroutine below for a fade/bounce tween later if wanted - this is deliberately the simplest thing that works for now.")]
        [SerializeField] private float deltaVisibleSeconds = 1f;

        private readonly Dictionary<string, Coroutine> _deltaClearRoutines = new Dictionary<string, Coroutine>();

        // Called on the very first refresh (panel open) so numbers are
        // correct immediately, with no delta shown (nothing "changed" yet).
        public void SetInitialResource(string resourceId, int total)
        {
            var slot = resourceSlots.Find(s => s.resourceId == resourceId);
            if (slot == null) return;

            if (slot.valueText != null) slot.valueText.text = total.ToString();
            if (slot.deltaText != null) slot.deltaText.text = string.Empty;
        }

        // Called whenever SectResourceChangedMessage arrives - the message
        // already carries the delta, no client-side "previous value"
        // tracking needed (unlike the old polling-based SectHudView).
        public void UpdateResource(string resourceId, int newTotal, int delta)
        {
            var slot = resourceSlots.Find(s => s.resourceId == resourceId);
            if (slot == null) return;

            if (slot.valueText != null) slot.valueText.text = newTotal.ToString();

            if (slot.deltaText == null) return;

            if (delta == 0)
            {
                slot.deltaText.text = string.Empty;
                return;
            }

            slot.deltaText.text = delta > 0 ? $"+{delta}" : delta.ToString();
            slot.deltaText.color = delta > 0 ? deltaPositiveColor : deltaNegativeColor;

            // Restart the clear timer on every change instead of letting
            // multiple timers stack up if the resource changes again before
            // the previous one finishes.
            if (_deltaClearRoutines.TryGetValue(resourceId, out var running) && running != null)
            {
                StopCoroutine(running);
            }
            _deltaClearRoutines[resourceId] = StartCoroutine(ClearDeltaAfterDelay(slot.deltaText));
        }

        private IEnumerator ClearDeltaAfterDelay(TMP_Text deltaText)
        {
            yield return new WaitForSeconds(deltaVisibleSeconds);
            if (deltaText != null) deltaText.text = string.Empty;
        }

        public void UpdateWallet(long spiritStones, long contribution)
        {
            if (spiritStonesText != null) spiritStonesText.text = spiritStones.ToString();
            if (contributionText != null) contributionText.text = contribution.ToString();
        }
    }
}
