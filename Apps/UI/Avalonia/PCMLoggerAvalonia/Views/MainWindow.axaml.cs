using Avalonia.Controls;
using Avalonia.Input;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void GaugeCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GaugeTile tile)
        {
            await new GaugeEditDialog(tile).ShowDialog(this);
        }
    }
}
