using System;
using MessagePipe;
using VContainer.Unity;
using Xianxia.Sect.Messages;
using UnityEngine;

namespace Xianxia.Sect.UI
{
    // Auto-opens the EventPopup panel whenever a decision-requiring world
    // event fires - the in-game-visible counterpart to what the MCP bridge
    // gets via await_next_world_event.
    public class WorldEventUISystem : IStartable, IDisposable
    {
        private readonly ISubscriber<WorldEventTriggeredMessage> _subscriber;
        private readonly UIService _uiService;
        private IDisposable _subscription;
        private readonly System.Collections.Generic.Queue<EventPopupOpenArgs> pending = new System.Collections.Generic.Queue<EventPopupOpenArgs>();

        public WorldEventUISystem(ISubscriber<WorldEventTriggeredMessage> subscriber, UIService uiService)
        {
            _subscriber = subscriber;
            _uiService = uiService;
        }

        public void Start()
        {
            _subscription = _subscriber.Subscribe(OnWorldEvent);
            Canvas.willRenderCanvases += FlushPending;
        }

        private void OnWorldEvent(WorldEventTriggeredMessage message)
        {
            if (!message.RequiresDecision) return;

            pending.Enqueue(new EventPopupOpenArgs
            {
                EventId = message.EventId,
                Description = message.Description,
                Choices = message.Choices,
            });
            FlushPending();
        }

        private void FlushPending()
        {
            if (DiscipleModalScope.IsOpen || pending.Count == 0) return;
            foreach (var popup in UnityEngine.Object.FindObjectsByType<EventPopupView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (popup.isActiveAndEnabled) return;
            var args = pending.Peek();
            try
            {
                _uiService.Open("EventPopup", args);
                pending.Dequeue();
            }
            catch (Exception ex)
            {
                // MessagePipe subscription callbacks can swallow exceptions
                // silently depending on how they're invoked - log loudly so
                // "the popup just doesn't show up" isn't a silent failure.
                Debug.LogError($"[WorldEventUISystem] Failed to open EventPopup for event '{args.EventId}': {ex}");
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            Canvas.willRenderCanvases -= FlushPending;
            pending.Clear();
        }
    }
}
