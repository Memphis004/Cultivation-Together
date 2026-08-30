namespace Xianxia.Sect.UI
{
    public class UIPanelHandle
    {
        public string PanelId { get; }
        public IUIView View { get; }
        public IUIViewPresenter Presenter { get; }

        internal UIPanelHandle(string panelId, IUIView view, IUIViewPresenter presenter)
        {
            PanelId = panelId;
            View = view;
            Presenter = presenter;
        }
    }
}
