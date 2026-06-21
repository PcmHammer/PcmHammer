using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadEmbeddedHtml("help.html", HelpWebBrowser);
            LoadEmbeddedHtml("credits.html", CreditsWebBrowser);
        }

        private void LoadEmbeddedHtml(string resourceName, WebBrowser browser)
        {
            try
            {
                // Format depends on your project structure. 
                // If files are in a folder named "Docs", use "pack://application:,,,/Docs/"
                Uri resourceUri = new Uri($"pack://application:,,,/{resourceName}", UriKind.Absolute);

                var streamInfo = Application.GetResourceStream(resourceUri);
                if (streamInfo != null)
                {
                    using (StreamReader reader = new StreamReader(streamInfo.Stream))
                    {
                        string htmlContent = reader.ReadToEnd();
                        // Display the raw HTML string directly
                        browser.NavigateToString(htmlContent);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading document: {ex.Message}");
            }
        }
    }
}