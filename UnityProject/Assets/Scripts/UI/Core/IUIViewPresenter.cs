using System;

namespace Xianxia.Sect.UI
{
    public interface IUIViewPresenter : IDisposable
    {
        void Bind(IUIView view);
        void OnOpen(object args);
        void OnClose();
    }
}
