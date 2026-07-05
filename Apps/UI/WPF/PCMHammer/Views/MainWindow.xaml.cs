using PCMHammer.Viewmodels;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PCMHammer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private bool _isCleanedUp = false;
        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainWindowViewModel(this);
            DataContext = _viewModel;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadEmbeddedHtml("help.html", HelpWebBrowser);
            LoadEmbeddedHtml("credits.html", CreditsWebBrowser);
        }

        private static void LoadEmbeddedHtml(string resourceName, WebBrowser browser)
        {
            try
            {
                // Format depends on your project structure. 
                // If files are in a folder named "Docs", use "pack://application:,,,/Docs/"
                Uri resourceUri = new($"pack://application:,,,/{resourceName}", UriKind.Absolute);

                var streamInfo = Application.GetResourceStream(resourceUri);
                if (streamInfo != null)
                {
                    using StreamReader reader = new(streamInfo.Stream);
                    string htmlContent = reader.ReadToEnd();
                    // Display the raw HTML string directly
                    browser.NavigateToString(htmlContent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading document: {ex.Message}");
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If we already finished cleaning up, let the window close normally
            if (_isCleanedUp) return;

            // Stop the window from closing immediately
            e.Cancel = true;

            if (_viewModel is not null)
            {
                _viewModel.StatusText = "Saving logs and cleaning up hardware connections...";

                // Await the shutdown process completely off the main UI thread
                await _viewModel.HandleApplicationShutdownAsync();
            }

            // Set flag and re-trigger close now that it's safe
            _isCleanedUp = true;
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(Close));
        }

        private void LogTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
                textBox.ScrollToEnd();
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
                textBox.ScrollToEnd();
        }
    }
}