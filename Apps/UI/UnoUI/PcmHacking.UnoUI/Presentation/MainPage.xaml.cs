using PcmHacking.UnoUI.Services;
using Windows.UI.Core;
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class MainPage : Page
{

    private Windows.System.Display.DisplayRequest _displayRequest;

    public MainPage()
    {
        this.InitializeComponent();
        this.ContentFrame.Navigate(typeof(MenuPage));
#if ANDROID
        SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
#endif
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        XamlRootService.Initialize(this.XamlRoot); 
        _displayRequest = new Windows.System.Display.DisplayRequest();
#if ANDROID
        _displayRequest.RequestActive();
        await Platforms.Android.PermissionMethods.RequestAndroidPermissions(); // Try, try again? We need to get file permissions before trying to extract kernels...
#endif
    }

    private void MainPage_BackRequested(object? sender, BackRequestedEventArgs e)
    {
        e.Handled = true;
        if (MainModel.CanGoBack)
        {
            this.ContentFrame.GoBack();
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
#if ANDROID
            _displayRequest.RequestRelease();
            SystemNavigationManager.GetForCurrentView().BackRequested -= MainPage_BackRequested;
#endif
    }

    public void FrameNavigated(object sender, NavigationEventArgs e)
    {
        (this.DataContext as MainViewModel)?.FrameNavigated.Execute(null);
    }
}
