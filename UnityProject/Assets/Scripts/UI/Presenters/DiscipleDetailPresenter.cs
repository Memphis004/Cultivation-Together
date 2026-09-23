using MessagePipe;
using Xianxia.Sect;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Phase 4 — presenter for the "click chibi → detail" panel (plan §11).
    /// Shows DisplayName / Rank / CurrentTask / Wallet + a Bust portrait bound to
    /// the SAME AvatarRenderer / AppearanceResolver pipeline as
    /// AvatarCustomization (no second portrait renderer — no drift).
    ///
    /// Plain C# class, Transient (instance per open — same shape as
    /// AvatarCustomizationPresenter); opened by DiscipleDetailUISystem when a
    /// chibi click publishes DiscipleSelectedMessage.
    /// </summary>
    public class DiscipleDetailPresenter : UIPresenter<DiscipleDetailView>
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly AvatarPartPool _partPool;

        public DiscipleDetailPresenter(ISectStateProvider stateProvider, AvatarPartPool partPool)
        {
            _stateProvider = stateProvider;
            _partPool = partPool;
        }

        protected override void OnViewBound()
        {
            View.CloseClicked += OnCloseClicked;
        }

        public override void OnOpen(object args)
        {
            var discipleId = args as string;
            if (string.IsNullOrEmpty(discipleId)) return;

            var d = _stateProvider.BuildSectEconomyState()?.Disciples?
                .Find(x => x != null && x.DiscipleId == discipleId);
            if (d == null) return;

            View.SetInfo(d.DisplayName, d.Rank.ToString(), d.CurrentTask,
                         "Spirit Stones: " + (d.Wallet?.SpiritStones ?? 0).ToString() +
                         " · Contribution: " + (d.Wallet?.Contribution ?? 0).ToString());

            // SAME portrait pipeline as AvatarCustomization: Initialize(pool) +
            // SetFraming(Bust) + SetAppearance — resolver identical by construction.
            var renderer = View.PortraitRenderer;
            if (renderer != null)
            {
                renderer.Initialize(_partPool);
                renderer.SetFraming(AvatarFraming.Bust);
                renderer.SetAppearance(d.Avatar != null ? d.Avatar.Clone() : new AvatarAppearance());
            }
        }

        private void OnCloseClicked()
        {
            View.Hide();
        }

        public override void Dispose()
        {
            View.CloseClicked -= OnCloseClicked;
        }
    }
}
