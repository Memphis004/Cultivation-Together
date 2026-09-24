using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // Same icon+value+delta pattern as the old SectHudView (now removed),
    // just driven by SectResourceChangedMessage via WalletHudPresenter
    // instead of polling ISectStateProvider every N seconds.
    [Serializable]
    public class ResourceSlotBinding
    {
        [Tooltip("Must match a key in SectEconomyState.Stockpile.RawResources, e.g. \"herb\"")]
        public string resourceId;
        public TMP_Text valueText;
        public TMP_Text deltaText;
    }

    public class WalletHudView : UIViewBase
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
        [SerializeField] private Image spiritStonesIcon;
        [SerializeField] private Image contributionIcon;

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

        // Data-driven layout (open question #13) - moved verbatim from
        // UIRoot.ApplyWalletHudLayout: top-stretch bar whose wallet cluster
        // hugs the top-RIGHT (mirrors the BottomMenu bar).
        public override void ApplyDefaultLayout()
        {
            // Top-stretch bar: full width, 100 px tall.
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 100f);
            rt.anchoredPosition = Vector2.zero;

            var hlg = GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.padding           = new RectOffset(20, 36, 10, 10); // right inset larger so the wallet cluster clears the screen edge
                hlg.spacing           = 12f;
                hlg.childAlignment    = TextAnchor.MiddleRight;
                hlg.childControlWidth  = true;
                hlg.childControlHeight = true;
                // Wallet-only panel: no force expand, so the pair hugs the
                // top-RIGHT as a compact cluster (mirror of BottomMenu).
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = false;
            }

            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;
            }

            foreach (Transform child in rt)
            {
                var cr = child.GetComponent<RectTransform>();
                if (cr == null) continue;
                cr.anchorMin = new Vector2(0.5f, 0.5f);
                cr.anchorMax = new Vector2(0.5f, 0.5f);
                cr.pivot     = new Vector2(0.5f, 0.5f);

                cr.sizeDelta = new Vector2(150f, 80f);
                if (child.name == "SpiritStonesText" || child.name == "ContributionText")
                {
                    var text = child.GetComponent<TMPro.TMP_Text>();
                    if (text != null)
                    {
                        text.alignment = TMPro.TextAlignmentOptions.MidlineRight;
                        text.margin = new Vector4(42f, 0f, 8f, 0f);
                    }
                }
            }
        }

        private void OnEnable()
        {
            ConfigureWalletIcon(spiritStonesIcon, spiritStonesText);
            ConfigureWalletIcon(contributionIcon, contributionText);
        }

        private static void ConfigureWalletIcon(Image icon, TMP_Text valueText)
        {
            if (icon == null) return;

            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.SetNativeSize();

            if (valueText != null)
            {
                var iconTransform = icon.rectTransform;
                if (iconTransform.parent != valueText.transform)
                    iconTransform.SetParent(valueText.transform, false);
                iconTransform.anchorMin = new Vector2(0f, 0.5f);
                iconTransform.anchorMax = new Vector2(0f, 0.5f);
                iconTransform.pivot = new Vector2(0f, 0.5f);
                iconTransform.anchoredPosition = new Vector2(0f, 0f);
                var nativeSize = icon.sprite != null ? icon.sprite.rect.size : iconTransform.sizeDelta;
                var scale = Mathf.Min(40f / nativeSize.y, 34f / nativeSize.x);
                iconTransform.sizeDelta = nativeSize * scale;
                valueText.alignment = TextAlignmentOptions.MidlineRight;
                valueText.margin = new Vector4(42f, 0f, 6f, 0f);
            }
        }
    }
}
