using Avalonia.Controls;

namespace PCMLoggerAvalonia.Views;

// Thin desktop host for MainView. The UI and its behaviour live in MainView (a UserControl) so the
// same content can run as a mobile single-view root; this window just hosts it on desktop.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
