// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PcmHacking.UnoUI.Utilities;

namespace PcmHacking.UnoUI.Presentation
{
    /// <summary>
    /// This page shows the progress of a RAM dump operation.
    /// </summary>
    public sealed partial class DumpRamPage : Page
	{
        private DumpRamModel? model;

        public DumpRamPage()
		{
			this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            var newModel = (this.DataContext as DumpRamViewModel)?.Model as DumpRamModel;
            if (newModel == null)
            {
                return;
            }

            if (newModel == this.model)
            {
                return;
            }

            this.model = newModel;

            // TODO: Auto-scroll
            // this.model.UserLog.ForEach((entry, token) => this.UserLogScrollViewer.ChangeView(0, this.UserLogScrollViewer.ScrollableHeight, 1), this.DispatcherQueue.GetForCurrentThread());
        }
    }
}
