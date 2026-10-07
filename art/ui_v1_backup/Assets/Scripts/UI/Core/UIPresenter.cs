using System;

namespace Xianxia.Sect.UI
{
    public abstract class UIPresenter<TView> : IUIViewPresenter
        where TView : class, IUIView
    {
        protected TView View { get; private set; }

        public void Bind(IUIView view)
        {
            View = view as TView;
            if (View == null)
            {
                throw new InvalidCastException(
                    $"Expected {typeof(TView).Name}, got {view.GetType().Name}");
            }
            OnViewBound();
        }

        protected virtual void OnViewBound() { }
        public virtual void OnOpen(object args) { }
        public virtual void OnClose() { }
        public virtual void Dispose() { }
    }
}
