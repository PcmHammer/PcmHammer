using Avalonia.Controls;
using PCMHammerAvalonia.Services;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(FileDialogService fileDialogService, SettingsService settingsService)
    {
        InitializeComponent();

        _viewModel = new SettingsViewModel(fileDialogService, settingsService);
        DataContext = _viewModel;

        _viewModel.RequestClose += () => { Close(false); };

        _viewModel.RequestAcceptandClose += () => { Close(true); };
    }

    // Default parameterless constructor required for Avalonia XAML Previewer
    public SettingsWindow()
    {
        InitializeComponent();
        _viewModel = null!;
    }
}