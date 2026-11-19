using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCMHammerAvalonia.ViewModels;

/// <summary>
/// Passively shows raw VPW or CAN traffic. Ports the Uno bus monitor; the capture itself is the shared
/// <see cref="BusMonitor"/>, this only drives it and displays the lines.
/// </summary>
public partial class BusMonitorViewModel : ObservableObject
{
    // A large PCM read is ~500k frames, so keep a bounded tail rather than the whole capture.
    private const int MaxLines = 5000;

    private readonly Vehicle _vehicle;
    private readonly ILogger _logger;
    private readonly Queue<string> _lines = new();
    private readonly object _linesLock = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _dirty;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial string Traffic { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Stopped.";

    [ObservableProperty]
    public partial string CanFilter { get; set; } = BusMonitor.DefaultCanFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    [NotifyPropertyChangedFor(nameof(CanFilterEnabled))]
    public partial bool IsRunning { get; set; }

    /// <summary>Which bus to watch: false is VPW, true is CAN.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseVpw))]
    [NotifyPropertyChangedFor(nameof(CanFilterEnabled))]
    public partial bool UseCan { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFilterEnabled))]
    public partial bool CanBusAvailable { get; set; } = true;

    [ObservableProperty]
    public partial bool VpwBusAvailable { get; set; } = true;

    public bool UseVpw
    {
        get => !UseCan;
        set => UseCan = !value;
    }

    public bool IsStopped => !IsRunning;
    public string StartStopText => IsRunning ? "Stop" : "Start";
    public bool CanFilterEnabled => UseCan && !IsRunning;

    public event Action? RequestClose;

    public BusMonitorViewModel(Vehicle vehicle, ILogger logger)
    {
        _vehicle = vehicle;
        _logger = logger;

        // Frames arrive far faster than the screen needs them. Batch the text updates on a timer
        // instead of pushing the whole buffer to the UI for every frame.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer.Tick += (_, _) => FlushTraffic();
        _refreshTimer.Start();

        RefreshCapability();
    }

    // Parameterless constructor for the XAML previewer.
    public BusMonitorViewModel()
    {
        _vehicle = null!;
        _logger = null!;
        _refreshTimer = new DispatcherTimer();
    }

    /// <summary>
    /// Find which buses this interface can watch and keep the selection on one. An unusable bus looks
    /// identical to a silent one otherwise.
    /// </summary>
    private void RefreshCapability()
    {
        IReadOnlyList<BusProtocol> supported = _vehicle.MonitorableProtocols;
        bool vpw = supported.Contains(BusProtocol.VPW);
        bool can = supported.Contains(BusProtocol.Can500k);

        VpwBusAvailable = vpw;
        CanBusAvailable = can;

        if (!vpw && can) UseCan = true;
        else if (!can && UseCan) UseCan = false;

        if (!vpw && !can) StatusText = "This interface cannot monitor either bus.";
    }

    // Deliberately synchronous. An async command's button is disabled while its handler runs, and this
    // is the same button that has to stop the capture, so the capture is started without being awaited.
    // RunAsync handles its own exceptions.
    [RelayCommand]
    private void StartStop()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            return;
        }

        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        BusProtocol protocol = UseCan ? BusProtocol.Can500k : BusProtocol.VPW;
        IReadOnlyCollection<uint>? canIds = UseCan ? BusMonitor.ParseCanIds(CanFilter) : null;

        // The 4X rule and its wording live in the library; this only shows the result.
        if (protocol == BusProtocol.VPW)
        {
            switch (BusMonitor.CheckVPWReadiness(_vehicle, out string readiness))
            {
                case VPWMonitorReadiness.NoFourXSupport:
                    _logger.AddUserMessage("Bus monitor: " + readiness);
                    break;

                case VPWMonitorReadiness.FourXDisabled:
                    StatusText = readiness.Replace(Environment.NewLine + Environment.NewLine, " ");
                    return;
            }
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        IsRunning = true;
        StatusText = $"Monitoring {protocol}...";
        _logger.AddUserMessage("Bus monitor started on " + protocol + ".");

        try
        {
            BusMonitor monitor = _vehicle.CreateBusMonitor();
            await Task.Run(() => monitor.RunAsync(protocol, canIds, AppendLine, token));
            StatusText = "Stopped.";
        }
        catch (OperationCanceledException)
        {
            // Stop was clicked; not an error.
            StatusText = "Stopped.";
        }
        catch (Exception exception)
        {
            _logger.AddUserMessage("Bus monitor error: " + exception.Message);
            _logger.AddDebugMessage(exception.ToString());
            StatusText = "Bus monitor error: " + exception.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            FlushTraffic();
            _logger.AddUserMessage("Bus monitor stopped.");
        }
    }

    /// <summary>Called on the capture thread for every frame, so it only queues.</summary>
    private void AppendLine(string line)
    {
        lock (_linesLock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines) _lines.Dequeue();
            _dirty = true;
        }
    }

    private void FlushTraffic()
    {
        string text;
        lock (_linesLock)
        {
            if (!_dirty) return;
            _dirty = false;
            text = string.Join(Environment.NewLine, _lines);
        }

        Traffic = text;
    }

    [RelayCommand]
    private void Clear()
    {
        lock (_linesLock)
        {
            _lines.Clear();
            _dirty = true;
        }

        FlushTraffic();
    }

    [RelayCommand]
    private void Close()
    {
        Stop();
        RequestClose?.Invoke();
    }

    /// <summary>Stops any capture and the refresh timer. Safe to call more than once.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _refreshTimer.Stop();
    }
}
