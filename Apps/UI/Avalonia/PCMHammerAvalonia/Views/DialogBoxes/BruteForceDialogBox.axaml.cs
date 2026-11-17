using Avalonia.Controls;
using PcmHacking;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class BruteForceDialogBox : Window
{
    private readonly BruteForceViewModel? _viewModel;

    public BruteForceDialogBox(Vehicle vehicle, ILogger logger)
    {
        InitializeComponent();

        _viewModel = new BruteForceViewModel(vehicle, logger);
        DataContext = _viewModel;

        _viewModel.RequestClose += Cancel;
        Closing += BruteForceDialogBox_Closing;
    }

    // Default parameterless constructor required for Avalonia XAML Previewer
    public BruteForceDialogBox()
    {
        InitializeComponent();
    }

    private void Cancel()
    {
        Close(true);
    }

    private void BruteForceDialogBox_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel != null && _viewModel.BruteForceRunning)
        {
            e.Cancel = true; // Block immediate window dismissal
            _viewModel.StopBruteForceCommand.Execute(null); // Signal background cancel loop
            _viewModel.StatusText = "Stopping...";
        }
    }
}