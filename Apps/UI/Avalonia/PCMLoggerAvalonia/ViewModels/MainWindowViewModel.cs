using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMLoggerAvalonia.Services;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// The logger's main view model. "Dumb front-end": it drives the shared PcmLibrary backend (device
/// connect, profile loading, LoggingController) and exposes observable state for the view. All the
/// protocol/logging work lives in PcmLibrary; this only connects the pieces and marshals rows to the
/// UI thread.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly StatusLogger _logger;

    private Device? _device;
    private Vehicle? _vehicle;
    private uint _osid;
    private ParameterDatabase? _database;
    private LogProfile? _profile;
    private LoggingController? _controller;

    // parameter id -> its row/gauge, so each reading updates in place rather than rebuilding the list.
    private readonly Dictionary<string, ReadingRow> _rowsById = new();

    // A parameter can drive more than one gauge (e.g. the same PID shown twice), so each id maps to a
    // list - otherwise repointing a gauge onto a parameter another gauge already shows leaves one unfed.
    private readonly Dictionary<string, List<GaugeTile>> _gaugesById = new();
    private readonly Dictionary<string, List<MonitorSeries>> _seriesByPid = new();

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    private string _profileName = "(no profile)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLogging;

    /// <summary>"Grid", "Dashboard" or "Monitor" - which view is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGrid))]
    [NotifyPropertyChangedFor(nameof(IsDashboard))]
    [NotifyPropertyChangedFor(nameof(IsMonitor))]
    private string _viewMode = "Grid";

    public bool IsGrid => this.ViewMode == "Grid";
    public bool IsDashboard => this.ViewMode == "Dashboard";
    public bool IsMonitor => this.ViewMode == "Monitor";

    public ObservableCollection<ReadingRow> Readings { get; } = new();

    /// <summary>Gauges from the shared dashboard layout (DefaultLoggerPackage).</summary>
    public ObservableCollection<GaugeTile> Gauges { get; } = new();

    /// <summary>Strip-chart monitors from the shared package (Engine, Fuelling).</summary>
    public ObservableCollection<MonitorTab> Monitors { get; } = new();

    [ObservableProperty]
    private MonitorTab? _selectedMonitor;

    /// <summary>When true the monitor view is frozen: new samples are not added to the traces.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private bool _isMonitorPaused;

    public string PauseButtonText => this.IsMonitorPaused ? "Resume" : "Pause";

    [RelayCommand]
    private void ToggleMonitorPause() => this.IsMonitorPaused = !this.IsMonitorPaused;

    [RelayCommand]
    private void NarrowMonitorWindow()
    {
        if (this.SelectedMonitor is MonitorTab monitor)
        {
            monitor.WindowSeconds = Math.Max(2, monitor.WindowSeconds * 0.8);
        }
    }

    [RelayCommand]
    private void WidenMonitorWindow()
    {
        if (this.SelectedMonitor is MonitorTab monitor)
        {
            monitor.WindowSeconds = Math.Min(300, monitor.WindowSeconds * 1.25);
        }
    }

    /// <summary>Every parameter the connected PCM supports, for the gauge parameter picker.</summary>
    public ObservableCollection<ParameterOption> AvailableParameters { get; } = new();

    /// <summary>Remove a gauge from the dashboard (and stop feeding it).</summary>
    public void RemoveGauge(GaugeTile tile)
    {
        this.Gauges.Remove(tile);
        this.ReindexGauges();
    }

    /// <summary>Rebuild the id-to-gauges map (after a gauge is added, removed or repointed).</summary>
    public void ReindexGauges()
    {
        _gaugesById.Clear();
        foreach (GaugeTile tile in this.Gauges)
        {
            if (string.IsNullOrEmpty(tile.ParameterId))
            {
                continue;
            }

            if (!_gaugesById.TryGetValue(tile.ParameterId, out List<GaugeTile>? list))
            {
                list = new List<GaugeTile>();
                _gaugesById[tile.ParameterId] = list;
            }

            list.Add(tile);
        }
    }

    [RelayCommand]
    private void ShowGrid() => this.ViewMode = "Grid";

    [RelayCommand]
    private void ShowDashboard() => this.ViewMode = "Dashboard";

    [RelayCommand]
    private void ShowMonitor() => this.ViewMode = "Monitor";

    public MainWindowViewModel()
    {
        // Library messages can arrive on background threads; marshal the status update to the UI.
        _logger = new StatusLogger(
            onUserMessage: message => Dispatcher.UIThread.Post(() => this.StatusText = message),
            onDebugMessage: _ => { });

        this.BuildFromPackage();
    }

    /// <summary>
    /// Build the gauge dashboard and the strip-chart monitors from the shared package (the same data
    /// the WinForms PcmLogger uses, which a TunerPro ADX import also produces) rather than inventing a
    /// front-end layout. Values arrive by parameter id from the logging stream.
    /// </summary>
    private void BuildFromPackage()
    {
        LoggerPackage package = DefaultLoggerPackage.Create();

        DashboardLayout? dashboard = package.Dashboards.FirstOrDefault();
        if (dashboard != null)
        {
            foreach (GaugeLayout layout in dashboard.Gauges)
            {
                this.Gauges.Add(new GaugeTile(layout));
            }

            this.ReindexGauges();
        }

        foreach (MonitorLayout monitorLayout in package.Monitors)
        {
            MonitorTab tab = new(monitorLayout);
            this.Monitors.Add(tab);
            foreach (MonitorSeries series in tab.Series)
            {
                if (string.IsNullOrEmpty(series.ParameterId))
                {
                    continue;
                }

                if (!_seriesByPid.TryGetValue(series.ParameterId, out List<MonitorSeries>? list))
                {
                    list = new List<MonitorSeries>();
                    _seriesByPid[series.ParameterId] = list;
                }

                list.Add(series);
            }
        }

        this.SelectedMonitor = this.Monitors.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (this.IsConnected)
        {
            return;
        }

        try
        {
            this.StatusText = "Looking for a serial interface...";

            string? portName = PickSerialPort();
            if (portName == null)
            {
                this.StatusText = "No serial ports found.";
                return;
            }

            this.StatusText = $"Detecting device on {portName}...";
            _device = await DeviceFactory.AutoDetectSerialDevice(portName, _logger);
            if (_device == null)
            {
                this.StatusText = $"No supported interface found on {portName}.";
                return;
            }

            if (!await _device.Initialize())
            {
                this.StatusText = $"Could not initialize {_device.GetDeviceType()}.";
                _device = null;
                return;
            }

            Protocol protocol = new();
            ToolPresentNotifier notifier = new(_device, protocol, _logger);
            _vehicle = new Vehicle(_device, protocol, _logger, notifier, string.Empty);

            Response<uint> osResponse = await _vehicle.QueryOperatingSystemId(CancellationToken.None);
            if (osResponse.Status != ResponseStatus.Success)
            {
                this.StatusText = $"Connected to {_device.GetDeviceType()}, but could not identify the PCM.";
                return;
            }

            _osid = osResponse.Value;

            _database = new ParameterDatabase(AppContext.BaseDirectory);
            _database.LoadDatabase();

            this.AvailableParameters.Clear();
            foreach (Parameter parameter in _database.ListParametersBySupportedOs(_osid).OrderBy(p => p.Name))
            {
                string units = parameter.Conversions.FirstOrDefault()?.Units ?? string.Empty;
                this.AvailableParameters.Add(new ParameterOption(parameter.Id, parameter.Name, units));
            }

            this.IsConnected = true;
            this.StatusText = $"Connected: {_device.GetDeviceType()}, OSID {_osid}.";
        }
        catch (Exception exception)
        {
            this.StatusText = "Connect failed: " + exception.Message;
        }
    }

    [RelayCommand]
    private async Task OpenProfile()
    {
        if (!this.IsConnected || _database == null)
        {
            this.StatusText = "Connect to a PCM before opening a profile.";
            return;
        }

        try
        {
            IStorageProvider? storage = GetStorageProvider();
            if (storage == null)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a log profile",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Log profiles") { Patterns = new[] { "*.LogProfile" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                },
            });

            if (files.Count == 0)
            {
                return;
            }

            string path = files[0].Path.LocalPath;

            LogProfileReader reader = new(_database, _osid, _logger);
            _profile = reader.Read(path);

            // The grid follows the profile (what is actually polled); the gauge dashboard is the shared
            // layout built once in the constructor and fed by parameter id.
            this.Readings.Clear();
            _rowsById.Clear();
            foreach (LogColumn column in _profile.Columns)
            {
                string id = column.Parameter.Id;
                ReadingRow row = new(id, column.Parameter.Name, column.Conversion.Units);
                this.Readings.Add(row);
                _rowsById[id] = row;
            }

            this.ProfileName = System.IO.Path.GetFileName(path);
            this.StatusText = $"Loaded profile '{this.ProfileName}' ({this.Readings.Count} parameters).";
        }
        catch (Exception exception)
        {
            this.StatusText = "Open profile failed: " + exception.Message;
        }
    }

    [RelayCommand]
    private void Start()
    {
        if (_vehicle == null || _profile == null || this.IsLogging)
        {
            this.StatusText = "Connect and open a profile first.";
            return;
        }

        _controller = new LoggingController(_vehicle, _osid, _logger);
        _controller.RowAvailable += OnRowAvailable;
        _controller.Stopped += () => Dispatcher.UIThread.Post(() => this.IsLogging = false);
        _controller.Failed += exception =>
            Dispatcher.UIThread.Post(() => this.StatusText = "Logging error: " + exception.Message);

        _controller.Start(_profile.Columns.ToList());
        this.IsLogging = true;
        this.StatusText = "Logging...";
    }

    [RelayCommand]
    private async Task Stop()
    {
        if (_controller != null)
        {
            await _controller.StopAsync();
            _controller.RowAvailable -= OnRowAvailable;
            _controller = null;
        }

        this.IsLogging = false;
        this.StatusText = this.IsConnected ? "Stopped." : "Not connected.";
    }

    private void OnRowAvailable(IReadOnlyList<LogRowElement> row)
    {
        // Fired on the controller's background thread; update bound values on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (LogRowElement element in row)
            {
                if (_rowsById.TryGetValue(element.ParameterId, out ReadingRow? target))
                {
                    target.Value = element.ValueAsString;
                }

                if (_gaugesById.TryGetValue(element.ParameterId, out List<GaugeTile>? gauges))
                {
                    foreach (GaugeTile gauge in gauges)
                    {
                        gauge.SetValue(element.ValueAsNumber);
                        // Keep the units label matching the conversion the profile actually logs.
                        gauge.Units = element.Units;
                    }
                }

                if (!this.IsMonitorPaused && _seriesByPid.TryGetValue(element.ParameterId, out List<MonitorSeries>? seriesList))
                {
                    foreach (MonitorSeries series in seriesList)
                    {
                        series.Append(element.ValueAsNumber);
                    }
                }
            }
        });
    }

    private static string? PickSerialPort()
    {
        List<SerialPortInfo> ports = PortDiscovery.GetPorts(NullLogger).ToList();
        if (ports.Count == 0)
        {
            return null;
        }

        // Prefer a USB CDC / ACM device (the OBDX and similar) over a legacy ttyS* port.
        SerialPortInfo? preferred = ports.FirstOrDefault(p =>
            (p.PortName ?? string.Empty).IndexOf("ACM", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (p.PortName ?? string.Empty).IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);

        return (preferred ?? ports[0]).PortName;
    }

    private static readonly ILogger NullLogger = new StatusLogger(_ => { }, _ => { });

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.StorageProvider;
        }

        return null;
    }
}
