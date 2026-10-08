using System;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // MVP Lite presenter for the bottom menu bar. Visual reskin scope only:
    // it exposes the raw Buttons so a later task can wire decisions/messages.
    // No message subscriptions yet — nothing to mutate until buttons get
    // real gameplay handlers.
    public class BottomMenuPresenter : UIPresenter<BottomMenuView>
    {
        public event Action BuildClicked;
        public event Action DiscipleClicked;

        /// <summary>Placeholder warehouse button (no asset yet) - UIBootstrap
        /// wires it to open the resource popup; kept out of the click-event
        /// pair above until it gets a real gameplay action.</summary>
        public Button WarehouseButton => View?.WarehouseButton;

        /// <summary>Exposed for UIBootstrap's build-mode toggle; BuildClicked
        /// event stays for future gameplay handlers.</summary>
        public Button BuildButton => View?.BuildButton;

        /// <summary>P3: discoverable open button for the TaskAssignment panel —
        /// the placeholder "วิถีเซียน" button (no other gameplay use yet).
        /// Same wiring shape as WarehouseButton.</summary>
        public Button TaskAssignmentButton => View?.ImmortalWayButton;

        protected override void OnViewBound()
        {
            if (View.BuildButton != null)
                View.BuildButton.onClick.AddListener(OnBuildClicked);
            if (View.DiscipleButton != null)
                View.DiscipleButton.onClick.AddListener(OnDiscipleClicked);
        }

        private void OnBuildClicked() => BuildClicked?.Invoke();
        private void OnDiscipleClicked() => DiscipleClicked?.Invoke();

        public override void Dispose()
        {
            if (View != null)
            {
                if (View.BuildButton != null)
                    View.BuildButton.onClick.RemoveListener(OnBuildClicked);
                if (View.DiscipleButton != null)
                    View.DiscipleButton.onClick.RemoveListener(OnDiscipleClicked);
            }
            BuildClicked = null;
            DiscipleClicked = null;
        }
    }
}
