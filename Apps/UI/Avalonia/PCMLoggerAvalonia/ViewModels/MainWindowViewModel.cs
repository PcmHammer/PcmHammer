using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    // The package the dashboard/monitors came from, so Save can update its gauges and write it back.
    private LoggerPackage _sourcePackage = DefaultLoggerPackage.Create();

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    private string _profileName = "(no profile)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLogging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    private bool _isRecording;

    public string RecordButtonText => this.IsRecording ? "Stop Recording" : "Record";

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

    // Device selection (from the Select Device dialog). Null port = first found; null type = auto-detect.
    public string? SelectedPortName { get; set; }
    public string? SelectedDeviceType { get; set; }
    public bool UseJ2534 { get; set; }
    public string? SelectedJ2534Device { get; set; }
    public bool Enable4x { get; set; }

    /// <summary>J2534 pass-through drivers only exist on Windows.</summary>
    public static bool IsJ2534Supported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>The serial ports currently present, for the Select Device dialog.</summary>
    public IReadOnlyList<SerialPortInfo> GetSerialPorts() => PortDiscovery.GetPorts(_logger).ToList();

    /// <summary>The installed J2534 devices (Windows only; empty elsewhere).</summary>
    public IReadOnlyList<string> GetJ2534Devices()
    {
        if (!IsJ2534Supported)
        {
            return Array.Empty<string>();
        }

        try
        {
            return J2534DeviceFinder.FindInstalledJ2534DLLs(_logger).Select(d => d.Name).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Add a new gauge at a default spot (the caller opens the editor to configure it).</summary>
    public GaugeTile AddGauge()
    {
        ParameterOption? first = this.AvailableParameters.FirstOrDefault();
        GaugeLayout layout = new()
        {
            Kind = GaugeKind.Round,
            PidId = first?.Id ?? string.Empty,
            Title = first?.Name ?? "New gauge",
            Units = first?.Units ?? string.Empty,
            RangeLow = 0,
            RangeHigh = 100,
            Digits = 1,
            HasAlarms = false,
            Left = 0.40,
            Top = 0.40,
            Width = 0.20,
            Height = 0.28,
        };

        GaugeTile tile = new(layout);
        this.Gauges.Add(tile);
        this.ReindexGauges();
        return tile;
    }

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
    private async Task LoadDashboard()
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load a dashboard",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Dashboards (.plz, .adx)") { Patterns = new[] { "*.plz", "*.adx" } },
                new FilePickerFileType("PcmLogger dashboards (.plz)") { Patterns = new[] { "*.plz" } },
                new FilePickerFileType("TunerPro definitions (.adx)") { Patterns = new[] { "*.adx" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        string path = files[0].Path.LocalPath;
        try
        {
            bool isAdx = Path.GetExtension(path).Equals(".adx", StringComparison.OrdinalIgnoreCase);
            LoggerPackage package = isAdx
                ? LoggerPackage.FromAdx(AdxDocument.Load(path), Path.GetFileName(path))
                : PlzFormat.Load(path);

            this.BuildFromPackage(package);
            this.StatusText = isAdx
                ? $"Imported TunerPro dashboard '{Path.GetFileName(path)}'."
                : $"Loaded dashboard '{Path.GetFileName(path)}'.";
        }
        catch (Exception exception)
        {
            this.StatusText = "Load dashboard failed: " + exception.Message;
        }
    }

    [RelayCommand]
    private async Task SaveDashboard()
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage == null)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save dashboard",
            DefaultExtension = "plz",
            SuggestedFileName = "dashboard.plz",
            FileTypeChoices = new[] { new FilePickerFileType("PcmLogger dashboards (.plz)") { Patterns = new[] { "*.plz" } } },
        });

        if (file == null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        try
        {
            // Update the source package's dashboard with the current gauges, then write it back.
            DashboardLayout? dashboard = _sourcePackage.Dashboards.FirstOrDefault();
            if (dashboard != null)
            {
                dashboard.Gauges.Clear();
                foreach (GaugeTile tile in this.Gauges)
                {
                    dashboard.Gauges.Add(tile.ToLayout());
                }
            }

            PlzFormat.Save(path, _sourcePackage);
            this.StatusText = $"Saved dashboard to '{Path.GetFileName(path)}'.";
        }
        catch (Exception exception)
        {
            this.StatusText = "Save dashboard failed: " + exception.Message;
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

        this.BuildFromPackage(DefaultLoggerPackage.Create());
    }

    /// <summary>
    /// Build the gauge dashboard and the strip-chart monitors from a shared package (the default, a
    /// loaded .plz, or a TunerPro .adx) rather than a front-end-invented layout. Values arrive by
    /// parameter id from the logging stream.
    /// </summary>
    private void BuildFromPackage(LoggerPackage package)
    {
        _sourcePackage = package;

        this.Gauges.Clear();
        this.Monitors.Clear();
        _gaugesById.Clear();
        _seriesByPid.Clear();

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
            if (this.UseJ2534)
            {
                this.StatusText = $"Opening J2534 device {this.SelectedJ2534Device}...";
                _device = DeviceFactory.CreateJ2534Device(this.SelectedJ2534Device, _logger);
                if (_device == null)
                {
                    this.StatusText = "Could not open the selected J2534 device.";
                    return;
                }
            }
            else
            {
                this.StatusText = "Looking for a serial interface...";

                string? portName = this.SelectedPortName ?? PickSerialPort();
                if (portName == null)
                {
                    this.StatusText = "No serial ports found - plug in an interface or use Select Device.";
                    return;
                }

                if (this.SelectedDeviceType == null)
                {
                    this.StatusText = $"Detecting device on {portName}...";
                    _device = await DeviceFactory.AutoDetectSerialDevice(portName, _logger);
                }
                else
                {
                    this.StatusText = $"Opening {this.SelectedDeviceType} on {portName}...";
                    _device = DeviceFactory.CreateSerialDevice(portName, this.SelectedDeviceType, _logger);
                }

                if (_device == null)
                {
                    this.StatusText = $"No supported interface found on {portName}.";
                    return;
                }
            }

            if (!await _device.Initialize())
            {
                this.StatusText = $"Could not initialize {_device.GetDeviceType()}.";
                _device = null;
                return;
            }

            Protocol protocol = new();
            ToolPresentNotifier notifier = new(_device, protocol, _logger);
            _vehicle = new Vehicle(_device, protocol, _logger, notifier, string.Empty)
            {
                Enable4xReadWrite = this.Enable4x,
            };

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
        _controller.RecordingStarted += path =>
            Dispatcher.UIThread.Post(() =>
            {
                this.IsRecording = true;
                this.StatusText = "Recording to " + path;
            });
        _controller.RecordingStopped += rows =>
            Dispatcher.UIThread.Post(() =>
            {
                this.IsRecording = false;
                this.StatusText = $"Recording stopped - {rows} rows saved.";
            });

        _controller.Start(_profile.Columns.ToList());
        this.IsLogging = true;
        this.StatusText = "Logging...";
    }

    [RelayCommand]
    private async Task ToggleRecording()
    {
        if (_controller == null || !this.IsLogging)
        {
            this.StatusText = "Start logging before recording.";
            return;
        }

        if (_controller.IsRecording)
        {
            _controller.StopRecording();
            return;
        }

        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "PcmLog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            await _controller.StartRecordingAsync(path);
        }
        catch (Exception exception)
        {
            this.StatusText = "Could not start recording: " + exception.Message;
        }
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
        this.IsRecording = false;
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
