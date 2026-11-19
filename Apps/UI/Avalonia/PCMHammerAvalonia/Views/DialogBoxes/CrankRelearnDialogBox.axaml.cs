using Avalonia.Controls;
using PcmHacking;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class CrankRelearnDialogBox : Window
{
    private readonly CrankRelearnViewModel? _viewModel;

    public CrankRelearnDialogBox(Vehicle vehicle, ILogger logger)
    {
        InitializeComponent();

        _viewModel = new CrankRelearnViewModel(vehicle, logger);
        DataContext = _viewModel;

        // Closing the window must stop polling the PCM so the device is released.
        Closing += (_, _) => _viewModel.Stop();
    }

    // Default parameterless constructor required for the Avalonia XAML previewer.
    public CrankRelearnDialogBox()
    {
        InitializeComponent();
    }
}
