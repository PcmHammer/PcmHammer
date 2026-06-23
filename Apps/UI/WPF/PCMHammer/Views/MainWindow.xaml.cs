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
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
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
    }
}