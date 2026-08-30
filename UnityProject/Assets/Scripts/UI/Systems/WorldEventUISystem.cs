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

        public WorldEventUISystem(ISubscriber<WorldEventTriggeredMessage> subscriber, UIService uiService)
        {
            _subscriber = subscriber;
            _uiService = uiService;
        }

        public void Start()
        {
            _subscription = _subscriber.Subscribe(OnWorldEvent);
        }

        private void OnWorldEvent(WorldEventTriggeredMessage message)
        {
            if (!message.RequiresDecision) return;

            try
            {
                _uiService.Open("EventPopup", new EventPopupOpenArgs
                {
                    EventId = message.EventId,
                    Description = message.Description,
                    Choices = message.Choices,
                });
            }
            catch (Exception ex)
            {
                // MessagePipe subscription callbacks can swallow exceptions
                // silently depending on how they're invoked - log loudly so
                // "the popup just doesn't show up" isn't a silent failure.
                Debug.LogError($"[WorldEventUISystem] Failed to open EventPopup for event '{message.EventId}': {ex}");
            }
        }

        public void Dispose()
        {
            _subscription?.Dispose();
        }
    }
}
