
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views;

public partial class MainView : UserControl
{
    /// <summary>
    /// The live mobile view, so the (Window-less) view model can reach a TopLevel to host overlay
    /// dialogs (e.g. the device picker) on Android. Null on desktop, where a Window owns dialogs.
    /// </summary>
    public static MainView? Current { get; private set; }

    public MainView()
    {
        InitializeComponent();
        Current = this;

        InitializeWebViews();
    }

    private void InitializeWebViews()
    {
        if (Design.IsDesignMode)
        {
            HelpViewContainer.Background = Avalonia.Media.Brushes.LightGray;
            HelpViewContainer.Child = new TextBlock
            {
                Text = "Help content would be displayed here.",
                Foreground = Avalonia.Media.Brushes.DarkGray,
                FontSize = 24,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            CreditsViewContainer.Background = Avalonia.Media.Brushes.LightGray;
            CreditsViewContainer.Child = new TextBlock
            {
                Text = "Credits content would be displayed here.",
                Foreground = Avalonia.Media.Brushes.DarkGray,
                FontSize = 24,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            return;
        }

#if ANDROID
        // Avalonia.Controls.WebView is desktop-only and is not referenced on Android; show the Help
        // and Credits content as plain text there instead of a native browser.
        HelpViewContainer.Child = new TextBlock
        {
            Text = "Help is available in the desktop app.",
            Margin = new Thickness(16),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        CreditsViewContainer.Child = new TextBlock
        {
            Text = "Credits are available in the desktop app.",
            Margin = new Thickness(16),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
#else
        var helpWebView = new NativeWebView();
        var creditsWebView = new NativeWebView();

        LoadEmbeddedHtml("help.html", helpWebView);
        LoadEmbeddedHtml("credits.html", creditsWebView);

        HelpViewContainer.Child = helpWebView;
        CreditsViewContainer.Child = creditsWebView;
#endif
    }

#if !ANDROID
    private static void LoadEmbeddedHtml(string resourceName, NativeWebView browser)
    {
        try
        {
            Uri resourceUri = new($"avares://PCMHammerAvaloniaUI/{resourceName}", UriKind.Absolute);

            if (AssetLoader.Exists(resourceUri))
            {
                using Stream stream = AssetLoader.Open(resourceUri);
                using StreamReader reader = new(stream);
                string htmlContent = reader.ReadToEnd();

                // Pass HTML string using proper UTF-8 data URI encoding
                browser.Source = new Uri($"data:text/html;charset=utf-8,{Uri.EscapeDataString(htmlContent)}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Resource not found: {resourceUri}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading document: {ex.Message}");
        }
    }
#endif

}