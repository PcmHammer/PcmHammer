// SPDX-License-Identifier: GPL-3.0-only
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Windows;

namespace PCMHammer.Viewmodels;

/// <summary>
/// The Bus Monitor tab, ported from the WinForms tab of the same name. It owns the device while
/// running, so the host locks the rest of the UI until the user presses Stop. What to monitor - the
/// default filter, filter parsing, the VPW 4X rule - lives in the library's BusMonitor.
/// </summary>
public partial class BusMonitorViewModel : ObservableObject, IDisposable
{
    private readonly ILogger _logger;

    // A callback rather than a back-reference: the monitor doesn't need to know its host.
    private readonly Action<bool> _setHostBusy;

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _runningTask;

    public BusMonitorViewModel(ILogger logger, Action<bool> setHostBusy)
    {
        _logger = logger;
        _setHostBusy = setHostBusy;
    }

    #region Properties

    /// <summary>Pushed in by the host when a device is selected or re-initialized.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOrStop))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    public partial Vehicle? Vehicle { get; set; }

    /// <summary>True while the host runs a read/write/verify, which would fight us for the device.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOrStop))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    public partial bool IsHostBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOrStop))]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    [NotifyPropertyChangedFor(nameof(IsVpwSelectable))]
    [NotifyPropertyChangedFor(nameof(IsCanSelectable))]
    [NotifyPropertyChangedFor(nameof(IsFilterEnabled))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    public partial bool IsRunning { get; set; }

    /// <summary>One flag, not one per protocol; the VPW radio binds through InverseBoolean.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterEnabled))]
    public partial bool IsCanSelected { get; set; }

    [ObservableProperty]
    public partial string CanFilterText { get; set; } = BusMonitor.DefaultCanFilter;

    /// <summary>The pane appends from this; it never changes identity, so no notification.</summary>
    public LogTextBuffer Lines { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOrStop))]
    [NotifyPropertyChangedFor(nameof(IsVpwSelectable))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    public partial bool IsVpwAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOrStop))]
    [NotifyPropertyChangedFor(nameof(IsCanSelectable))]
    [NotifyPropertyChangedFor(nameof(IsFilterEnabled))]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    public partial bool IsCanAvailable { get; set; }

    public bool IsVpwSelectable => IsVpwAvailable && !IsRunning;

    public bool IsCanSelectable => IsCanAvailable && !IsRunning;

    public bool IsFilterEnabled => IsCanSelected && IsCanAvailable && !IsRunning;

    public string StartStopText => IsRunning ? "Stop" : "Start";

    /// <summary>Stays true while running so Stop still works.</summary>
    public bool CanStartOrStop =>
        IsRunning || (Vehicle is not null && !IsHostBusy && (IsVpwAvailable || IsCanAvailable));

    #endregion

    /// <summary>Which protocols this device can monitor; keeps the selection on an available one.</summary>
    public void RefreshCapability()
    {
        if (IsRunning)
        {
            return;
        }

        IReadOnlyList<BusProtocol> supported = Vehicle?.MonitorableProtocols ?? Array.Empty<BusProtocol>();
        IsVpwAvailable = supported.Contains(BusProtocol.Vpw);
        IsCanAvailable = supported.Contains(BusProtocol.Can500k);

        if (IsCanSelected && !IsCanAvailable)
        {
            IsCanSelected = false;
        }
        else if (!IsCanSelected && !IsVpwAvailable)
        {
            IsCanSelected = IsCanAvailable;
        }
    }

    partial void OnVehicleChanged(Vehicle? value) => RefreshCapability();

    partial void OnIsHostBusyChanged(bool value)
    {
        if (!value)
        {
            RefreshCapability();
        }
    }

    #region Commands

    /// <summary>
    /// AllowConcurrentExecutions is required: an async [RelayCommand] reports CanExecute false while
    /// its task runs, which would grey out the button and leave no way to stop. The re-entrant call
    /// just cancels and returns.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartOrStop), AllowConcurrentExecutions = true)]
    public async Task StartStop()
    {
        if (IsRunning)
        {
            _cancellationTokenSource?.Cancel();
            return;
        }

        if (Vehicle is null)
        {
            return;
        }

        BusProtocol protocol = IsCanSelected ? BusProtocol.Can500k : BusProtocol.Vpw;

        if (protocol == BusProtocol.Vpw && !ConfirmVpwReadiness())
        {
            return;
        }

        IReadOnlyCollection<uint>? canIds = protocol == BusProtocol.Can500k
            ? BusMonitor.ParseCanIds(CanFilterText)
            : null;

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;
        _setHostBusy(true);
        Lines.Start();
        _logger.AddUserMessage("Bus monitor started on " + protocol + ".");

        BusMonitor monitor = Vehicle.CreateBusMonitor();
        try
        {
            _runningTask = Task.Run(() => monitor.RunAsync(
                protocol,
                canIds,
                line => Lines.Append(line),
                _cancellationTokenSource.Token));

            await _runningTask;
        }
        catch (OperationCanceledException)
        {
            // Stop was pressed, or the app is closing.
        }
        catch (Exception exception)
        {
            _logger.AddUserMessage("Bus monitor error: " + exception.Message);
            _logger.AddDebugMessage(exception.ToString());
        }
        finally
        {
            _runningTask = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            IsRunning = false;
            _setHostBusy(false);

            Lines.Flush();
            Lines.Stop();

            _logger.AddUserMessage("Bus monitor stopped.");
            RefreshCapability();
        }
    }

    [RelayCommand]
    public void ClearLog() => Lines.Clear();

    #endregion

    /// <summary>False only when the user can fix something first (4X supported but switched off).</summary>
    private bool ConfirmVpwReadiness()
    {
        switch (BusMonitor.CheckVpwReadiness(Vehicle!, out string message))
        {
            case VpwMonitorReadiness.NoFourXSupport:
                _logger.AddUserMessage("Bus monitor: " + message);
                return true;

            case VpwMonitorReadiness.FourXDisabled:
                MessageBox.Show(message, "Bus Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// Waits for the device to be released before shutdown disposes the Vehicle. Cancellation is only
    /// noticed between receives, so allow for one receive timeout.
    /// </summary>
    public async Task StopAsync()
    {
        _cancellationTokenSource?.Cancel();

        Task? running = _runningTask;
        if (running is null)
        {
            return;
        }

        try
        {
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Already reported by StartStop, or it didn't stop in time. Shutdown continues either way.
        }
    }

    public void Dispose() => Lines.Dispose();
}
