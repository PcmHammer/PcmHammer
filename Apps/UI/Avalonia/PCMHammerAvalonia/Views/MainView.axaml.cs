
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using PCMHammerAvalonia.ViewModels;
using System.ComponentModel;

namespace PCMHammerAvalonia.Views;

public partial class MainView : UserControl
{
    /// <summary>
    /// The live mobile view, so the (Window-less) view model can reach a TopLevel to host overlay
    /// dialogs (e.g. the device picker) on Android. Null on desktop, where a Window owns dialogs.
    /// </summary>
    public static MainView? Current { get; private set; }

    private readonly SelectableTextBlock _resultsLog = CreateLogTextBlock();
    private readonly SelectableTextBlock _debugLog = CreateLogTextBlock();
    private readonly ScrollViewer _resultsScroll;
    private readonly ScrollViewer _debugScroll;
    private MainWindowViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        Current = this;

        // The Results/Debug log views are built in code and hosted in their Border, the same way the
        // Help/Credits views are: a SelectableTextBlock bound inside a ContentPage via XAML does not
        // render on the mobile TabbedPage, but a code-set Border.Child does.
        _resultsScroll = WrapInScroll(_resultsLog);
        _debugScroll = WrapInScroll(_debugLog);
        ResultsViewContainer.Child = _resultsScroll;
        DebugViewContainer.Child = _debugScroll;

        this.DataContextChanged += OnDataContextChanged;

        InitializeWebViews();
    }

    private static SelectableTextBlock CreateLogTextBlock() => new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, monospace"),
        Margin = new Thickness(6),
    };

    private static ScrollViewer WrapInScroll(Control content) => new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Content = content,
    };

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateLogText();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.LogText) or nameof(MainWindowViewModel.DebugLogText))
        {
            Dispatcher.UIThread.Post(UpdateLogText);
        }
    }

    private void UpdateLogText()
    {
        if (_viewModel is null)
        {
            return;
        }

        _resultsLog.Text = _viewModel.LogText;
        _debugLog.Text = _viewModel.DebugLogText;
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