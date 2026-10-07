using System;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Phase 4 — opens the DiscipleDetail panel when the player clicks a chibi
    /// (ChibiClickTarget publishes DiscipleSelectedMessage, in-memory only).
    /// Mirrors WorldEventUISystem exactly: IStartable + one MessagePipe
    /// subscription + UIService.Open with try/catch loud logging.
    /// </summary>
    public class DiscipleDetailUISystem : IStartable, IDisposable
    {
        private readonly ISubscriber<DiscipleSelectedMessage> _subscriber;
        private readonly UIService _uiService;
        private IDisposable _subscription;

        public DiscipleDetailUISystem(ISubscriber<DiscipleSelectedMessage> subscriber, UIService uiService)
        {
            _subscriber = subscriber;
            _uiService = uiService;
        }

        public void Start()
        {
            _subscription = _subscriber.Subscribe(OnDiscipleSelected);
        }

        private void OnDiscipleSelected(DiscipleSelectedMessage message)
        {
            try
            {
                _uiService.Open("DiscipleDetail", message.DiscipleId);
            }
            catch (Exception ex)
            {
                // MessagePipe callbacks can swallow exceptions — log loudly so a
                // missing panel entry is never a silent failure (WorldEventUISystem lesson).
                Debug.LogError("[DiscipleDetailUISystem] Failed to open DiscipleDetail for '" +
                               message.DiscipleId + "': " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_subscription != null) _subscription.Dispose();
        }
    }
}
