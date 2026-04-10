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

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        XamlRootService.Initialize(this.XamlRoot);
#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
        {
            var result = global::Android.OS.Environment.IsExternalStorageManager;
            if (!result)
            {
                Droid.MainActivity.RequestFilePermisions();
            }
        }
        _displayRequest = new Windows.System.Display.DisplayRequest();
        _displayRequest.RequestActive();
#endif
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
