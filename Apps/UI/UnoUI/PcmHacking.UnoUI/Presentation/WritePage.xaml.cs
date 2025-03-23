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

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace PcmHacking.UnoUI.Presentation
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class WritePage : Page
    {
        private WriteModel model;

        public WritePage()
        {
            this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            var newModel = (this.DataContext as WriteViewModel)?.Model as WriteModel;
            if (newModel == null)
            {
                return;
            }

            if (newModel == this.model)
            {
                return;
            }

            this.model = newModel;

            this.model.UserLog.ForEach((value, cancellationToken) => await this.OnUserLogChanged(value, cancellationToken));
        }

        private void OnUserLogChanged(string value, CancellationToken cancellationToken)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                this.UserLogScrollViewer.ScrollToVerticalOffset(this.UserLogScrollViewer.ScrollableHeight);
            });
        }
    }
}
