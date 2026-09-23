using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 4 (§11) — click-to-select hit target attached to every chibi root
    /// GameObject (Sprite AND Spine backends) at spawn time.
    ///
    /// Constraint compliance:
    /// - Hit detection goes through Unity's normal Collider2D + OnMouseDown on the
    ///   visual's OWN GameObject — no FindObjectOfType, no reflection, no global
    ///   manager scanning (C12). The project's scenes use an orthographic 2D camera
    ///   (SpikeSceneBootstrap.EnsureSceneCamera precedent), so 2D physics is correct.
    /// - The publisher is constructor-injected at creation time by
    ///   DiscipleVisualSystem — never resolved from a static/service locator.
    /// - DiscipleSelectedMessage is in-memory only (MessagePipe), never on the
    ///   interprocess broker (same rule as every DiscipleVisualSystem message).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class ChibiClickTarget : MonoBehaviour
    {
        private IPublisher<DiscipleSelectedMessage> _publisher;
        private string _discipleId;

        /// <summary>Called once by DiscipleVisualSystem right after AddComponent.</summary>
        public void Bind(string discipleId, IPublisher<DiscipleSelectedMessage> publisher)
        {
            _discipleId = discipleId;
            _publisher = publisher;
        }

        private void OnMouseDown()
        {
            if (_publisher == null || string.IsNullOrEmpty(_discipleId)) return;
            _publisher.Publish(new DiscipleSelectedMessage { DiscipleId = _discipleId });
        }
    }
}
