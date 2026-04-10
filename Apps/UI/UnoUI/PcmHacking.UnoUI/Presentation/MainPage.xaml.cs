using PcmHacking.UnoUI.Services;
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class MainPage : Page
{

    private Windows.System.Display.DisplayRequest _displayRequest;

    public MainPage()
    {
        this.InitializeComponent();
        this.ContentFrame.Navigate(typeof(MenuPage));
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        XamlRootService.Initialize(this.XamlRoot); 
        _displayRequest = new Windows.System.Display.DisplayRequest();
        _displayRequest.RequestActive();
    }


    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
#if ANDROID
            _displayRequest.RequestRelease();
#endif
    }

    public void FrameNavigated(object sender, NavigationEventArgs e)
    {
        (this.DataContext as MainViewModel)?.FrameNavigated.Execute(null);
    }
}
