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
                // TODO: This definatatly has a better location.
        string[] KernelNames = ["Kernel-BlackBox.bin", "Kernel-P01.bin", "Kernel-P04.bin", "Kernel-P04_Early.bin", "Kernel-P05.bin", "Kernel-P08.bin", "Kernel-P10.bin", "Kernel-P11.bin", "Kernel-P12.bin", "Kernel-E54.bin", "Loader-P04.bin"];
        foreach(string kernel in KernelNames)
        {
            string directory = "/storage/emulated/0/PCMHammer/Bins";
            string filePath = $"{directory}/{kernel}";
            if (!File.Exists(filePath))
            {
                var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/BuildFiles/{kernel}"));
                var content = await file.OpenReadAsync();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(filePath, content.AsStream().ToMemoryStream().ToArray());
            }
        }
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
