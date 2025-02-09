using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PcmHacking.UnoUI.Presentation;
using Windows.Devices.Bluetooth.Background;

namespace PcmHacking.UnoUI.Presentation
{
    public sealed partial class CrankRelearnPage : Page
    {
        private CrankRelearnModel? model;

        public CrankRelearnPage()
        {
            this.InitializeComponent();

            this.DataContextChanged += (sender, e) =>
            {
                this.model = (this.DataContext as CrankRelearnViewModel)?.Model as CrankRelearnModel;
            };
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            this.model?.StopTimer();
        }
    }
}
