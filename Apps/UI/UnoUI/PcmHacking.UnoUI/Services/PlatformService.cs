// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PcmHacking.UnoUI
{
    public interface IPlatformService
    {
        void PrepareChildWindow(object target);
    }

    public class PlatformService : IPlatformService
    {
        public void PrepareChildWindow(object target)
        {
#if WINDOWS
            nint handle = WindowNative.GetWindowHandle(App.StaticMainWindow);
            InitializeWithWindow.Initialize(target, handle);
#endif
        }
    }
}
