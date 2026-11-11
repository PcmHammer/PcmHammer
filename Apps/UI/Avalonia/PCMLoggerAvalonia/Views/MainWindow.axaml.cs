using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    private async Task EditGaugeAsync(GaugeTile tile)
    {
        MainWindowViewModel? vm = this.DataContext as MainWindowViewModel;
        List<ParameterOption> options = vm?.AvailableParameters.ToList() ?? new List<ParameterOption>();
        await new GaugeEditDialog(tile, options).ShowDialog(this);

        // The parameter may have changed; re-point the feed.
        vm?.ReindexGauges();
    }

    private async void GaugeCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GaugeTile tile)
        {
            await this.EditGaugeAsync(tile);
        }
    }

    // Right-click a gauge: a small Edit / Remove menu (double-click still edits directly).
    private void GaugeCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not GaugeTile tile)
        {
            return;
        }

        if (!e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
        {
            return;
        }

        MenuFlyout flyout = new();

        MenuItem edit = new() { Header = "Edit gauge…" };
        edit.Click += async (_, _) => await this.EditGaugeAsync(tile);

        MenuItem remove = new() { Header = "Remove gauge" };
        remove.Click += (_, _) => (this.DataContext as MainWindowViewModel)?.RemoveGauge(tile);

        flyout.Items.Add(edit);
        flyout.Items.Add(remove);
        flyout.ShowAt(control, showAtPointer: true);
        e.Handled = true;
    }
}
