using Avalonia.Controls;
using PcmHacking;
using PCMHammerAvalonia.Services;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class DumpRamDialogBox : Window
{
    private readonly DumpRamViewModel? _viewModel;

    public DumpRamDialogBox(Vehicle vehicle, ILogger logger, IFileDialogService fileDialogService)
    {
        InitializeComponent();

        _viewModel = new DumpRamViewModel(vehicle, logger, fileDialogService);
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            Closing -= OnClosing;
            Close();
        };
        Closing += OnClosing;
    }

    // Default parameterless constructor required for the Avalonia XAML previewer.
    public DumpRamDialogBox()
    {
        InitializeComponent();
    }

    // Closing mid-dump must stop the read and release the device first, so hold the window open until
    // the view model says it is done.
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsRunning)
        {
            e.Cancel = true;
            _viewModel.CloseCommand.Execute(null);
        }
    }
}
