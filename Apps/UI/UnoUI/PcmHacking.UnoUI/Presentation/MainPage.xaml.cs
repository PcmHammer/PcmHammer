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
#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
        {
            var result = global::Android.OS.Environment.IsExternalStorageManager;
            if (!result)
            {
                Droid.MainActivity.RequestFilePermisions();
            }
        }
#endif
    }

    public void FrameNavigated(object sender, NavigationEventArgs e)
    {
        (this.DataContext as MainViewModel)?.FrameNavigated.Execute(null);
    }
}
