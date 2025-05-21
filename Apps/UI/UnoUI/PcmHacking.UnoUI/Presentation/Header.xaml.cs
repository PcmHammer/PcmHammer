using PcmHacking.UnoUI.Services;
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class Header : Page
{
    public Header()
    {
        this.InitializeComponent();
        this.ContentFrame.Navigate(typeof(MenuPage));
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        XamlRootService.Initialize(this.XamlRoot);
    }

    public void FrameNavigated(object sender, NavigationEventArgs e)
    {
        (this.DataContext as HeaderViewModel)?.FrameNavigated.Execute(null);
    }
}
