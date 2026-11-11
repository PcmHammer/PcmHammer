using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// The logger's main view model. "Dumb front-end": it drives the shared PcmLibrary logging backend
/// (device connect, profile, LoggingController) and exposes observable state for the view to bind to.
/// No protocol or logging logic lives here.
///
/// Stage A scaffold: layout, status and command stubs. Connect/OpenProfile/Start/Stop are wired to
/// the real device + LoggingController in the next milestone.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    private string _profileName = "(no profile)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLogging;

    /// <summary>Live readings, one row per logged parameter, updated in place each frame.</summary>
    public ObservableCollection<ReadingRow> Readings { get; } = new();

    [RelayCommand]
    private void Connect()
    {
        // TODO (milestone 2): open the device via the shared DeviceFactory, identify the PCM.
        this.StatusText = "Connect: not wired yet.";
    }

    [RelayCommand]
    private void OpenProfile()
    {
        // TODO (milestone 2): file picker -> LogProfileReader -> columns.
        this.StatusText = "Open Profile: not wired yet.";
    }

    [RelayCommand]
    private void Start()
    {
        // TODO (milestone 2): LoggingController.Start(columns), subscribe RowAvailable.
        this.StatusText = "Start: not wired yet.";
    }

    [RelayCommand]
    private void Stop()
    {
        // TODO (milestone 2): LoggingController.StopAsync().
        this.StatusText = "Stop: not wired yet.";
    }
}
