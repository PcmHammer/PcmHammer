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

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace PcmHacking.UnoUI.Presentation
{
    /// <summary>
    /// This page shows the progress of a flash write operation.
    /// </summary>
    /// <remarks>
    /// This is mostly duplicated in ReadPage.xaml.cs, but Uno didn't like it when I used a shared base class for both pages.
    /// TODO: try creating a single ReadWritePage/ReadWriteModel to eliminate the duplicated code.
    /// </remarks>
    public sealed partial class ControllerActionSetupPage : Page
    {
        private ControllerActionSetupModel? model;
        private bool _shouldGoBackAgain = false;

        public ControllerActionSetupPage()
        {
            this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
            this.Loaded += ControllerActionSetupPage_Loaded;
        }

        private void ControllerActionSetupPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_shouldGoBackAgain)
            {
                this.Frame.GoBack();
            }
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if(e.NavigationMode == NavigationMode.Back)
            {
                _shouldGoBackAgain = true;
            }
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            var newModel = (this.DataContext as ControllerActionSetupViewModel)?.Model as ControllerActionSetupModel;
            if (newModel == null)
            {
                return;
            }

            if (newModel == this.model)
            {
                return;
            }

            this.model = newModel;

            bool darkMode = this.XamlRoot == null ? false : SystemThemeHelper.IsRootInDarkMode(this.XamlRoot);
            ColorUtilities.Initialize(darkMode);
        }
    }
}
