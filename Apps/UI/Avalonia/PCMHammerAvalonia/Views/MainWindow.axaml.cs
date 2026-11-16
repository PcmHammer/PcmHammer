using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcmHacking.UI;
using PCMHammerAvalonia.ViewModels;
using System;

namespace PCMHammerAvalonia.Views;

public partial class MainWindow : ChromeWindow
{
    private readonly MainWindowViewModel _viewModel;
    private bool _isCleanedUp = false;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(this);
        DataContext = _viewModel;

        InitializeWebViews();
    }

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        // If cleanup has finished, allow the window to close normally
        if (_isCleanedUp) return;

        // Stop the window from closing immediately
        e.Cancel = true;

        if (_viewModel is not null)
        {
            _viewModel.StatusText = "Saving logs and cleaning up hardware connections...";

            // Await the shutdown process completely off the main UI thread
            await _viewModel.HandleApplicationShutdownAsync();
        }

        // Set flag and re-trigger close cleanly on the UI Thread
        _isCleanedUp = true;
        Dispatcher.UIThread.Post(Close);
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
        // and Credits content as plain text there instead of a native browser. (MainWindow itself is
        // not used on Android - the activity hosts MainView - but it must still compile for the head.)
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

    private void Border_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Don't drag if clicking on a MenuItem or any control within a menu
            if (!IsWithinMenuItem(e.Source as Visual))
            {
                BeginMoveDrag(e);
            }
        }
    }

    private static bool IsWithinMenuItem(Visual? source)
    {
        Visual? current = source;
        while (current != null)
        {
            if (current is MenuItem or Menu)
            {
                return true;
            }
            current = current.GetVisualParent();
        }
        return false;
    }
}