using Avalonia.Controls;
using PcmHacking;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class BusMonitorDialogBox : Window
{
    private readonly BusMonitorViewModel? _viewModel;

    public BusMonitorDialogBox(Vehicle vehicle, ILogger logger)
    {
        InitializeComponent();

        _viewModel = new BusMonitorViewModel(vehicle, logger);
        DataContext = _viewModel;

        // Closing the window must end the capture so the device is released.
        Closing += (_, _) => _viewModel.Stop();
    }

    // Default parameterless constructor required for the Avalonia XAML previewer.
    public BusMonitorDialogBox()
    {
        InitializeComponent();
    }
}
