using PcmHacking.UnoUI.Services;
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class MainPage : Page
{
    public MainPage()
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
        (this.DataContext as MainViewModel)?.FrameNavigated.Execute(null);
    }
}
