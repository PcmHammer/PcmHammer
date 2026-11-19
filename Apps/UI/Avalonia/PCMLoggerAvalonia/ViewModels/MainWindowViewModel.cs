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
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMLoggerAvalonia.Services;
// On the android target Android.App.Application is also in scope; this keeps Application meaning
// Avalonia's (harmless on desktop, where it is the only Application).
using Application = Avalonia.Application;

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

    // A synthetic "Query time" row pinned to the bottom of the live-value pane (the ms between rows),
    // as the WinForms logger shows. Not a real PID, so it is not in _rowsById; updated per row.
    private readonly ReadingRow _queryTimeRow = new("__querytime", "Query time", "ms");
    private DateTime _lastRowTime;

    // A parameter can drive more than one gauge (e.g. the same PID shown twice), so each id maps to a
    // list - otherwise repointing a gauge onto a parameter another gauge already shows leaves one unfed.
    private readonly Dictionary<string, List<GaugeTile>> _gaugesById = new();
    private readonly Dictionary<string, List<MonitorSeries>> _seriesByPid = new();

    // The package the dashboard/monitors came from, so Save can update its gauges and write it back.
    private LoggerPackage _sourcePackage = DefaultLoggerPackage.Create();

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileTitle))]
    private string _profileName = "(no profile)";

    // True once the Parameters tab has been changed since the profile was loaded or last saved.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileTitle))]
    private bool _isProfileModified;

    /// <summary>The profile name as shown to the user, with a star when it has unsaved changes.</summary>
    public string ProfileTitle => this.IsProfileModified ? this.ProfileName + " *" : this.ProfileName;

    // The file the current profile was opened from or last saved to. Null for the bundled default and
    // for a new profile, so Save asks where instead of overwriting the bundled Overview profile.
    private string? _profilePath;

    /// <summary>Asks the user to confirm (supplied by the view, which owns the dialogs). Returns true to
    /// go ahead. Unset means "just proceed", for a host with no dialog.</summary>
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    private bool _isConnected;

    /// <summary>The single Connect/Disconnect button's caption, as the WinForms logger does.</summary>
    public string ConnectButtonText => this.IsConnected ? "Disconnect" : "Connect";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopButtonText))]
    private bool _isLogging;

    /// <summary>The single Start/Stop Logging button's caption.</summary>
    public string StartStopButtonText => this.IsLogging ? "Stop Logging" : "Start Logging";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    private bool _isRecording;

    public string RecordButtonText => this.IsRecording ? "Stop Recording" : "Record";

    // --- Configuration tab (mirrors the WinForms PcmLogger Configuration tab) --------------------

    // The "View" checkboxes (same row/order as WinForms: Debug, Dashboard, Monitors, PIDs, Zoom).
    // PIDs toggles the always-visible live-value pane on the right; the others show/hide their tabs.
    // Configuration, Parameters and Trouble Codes are always available.
    [ObservableProperty] private bool _showDebug = true;
    [ObservableProperty] private bool _showDashboard = true;
    [ObservableProperty] private bool _showMonitors = true;
    [ObservableProperty] private bool _showPids = true;

    // Zoom shows the parameters flagged Zoom in a large white-on-black readout (the WinForms Zoom view).
    [ObservableProperty] private bool _showZoom;

    /// <summary>Enabled parameters flagged Zoom, for the big Zoom display. Shares the same ReadingRow
    /// instances as the live-value pane, so they update together.</summary>
    public ObservableCollection<ReadingRow> ZoomedReadings { get; } = new();

    [ObservableProperty] private bool _hasZoomedReadings;

    /// <summary>The library's debug messages, shown on the Debug tab.</summary>
    public ObservableCollection<string> DebugMessages { get; } = new();

    /// <summary>Every supported PID as a selectable row (the Parameters tab). Source of truth for what
    /// is logged - the enabled rows build the log columns.</summary>
    public ObservableCollection<ParameterRow> ParameterRows { get; } = new();

    /// <summary>Search text for the Parameters tab; rows whose name doesn't contain it are hidden.</summary>
    [ObservableProperty] private string _parameterFilter = string.Empty;

    partial void OnParameterFilterChanged(string value)
    {
        string needle = (value ?? string.Empty).Trim();
        foreach (ParameterRow row in this.ParameterRows)
        {
            row.MatchesFilter = needle.Length == 0
                || row.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // Where recordings are written (the "Paths" section). Defaults to the user's Documents folder,
    // as the WinForms logger does.
    [ObservableProperty] private string _logFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // Theme: "System" follows the OS; "Light"/"Dark" force a variant (FluentAvalonia honours it).
    [ObservableProperty] private string _selectedTheme = "System";

    public string[] ThemeOptions { get; } = { "System", "Light", "Dark" };

    partial void OnSelectedThemeChanged(string value)
    {
        if (Application.Current is Application app)
        {
            app.RequestedThemeVariant = value switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    // Start logging automatically once connected and a profile is open (the "Options" section).
    [ObservableProperty] private bool _autoStartLogging;

    // A short description of the chosen interface, shown in the Devices section.
    [ObservableProperty] private string _deviceSummary = "No device selected.";

    /// <summary>Recompose the Devices-section summary from the current selection.</summary>
    public void UpdateDeviceSummary()
    {
        if (this.UseJ2534)
        {
            this.DeviceSummary = string.IsNullOrEmpty(this.SelectedJ2534Device)
                ? "J2534 (no device selected)"
                : "J2534: " + this.SelectedJ2534Device;
        }
        else
        {
            this.DeviceSummary = string.IsNullOrEmpty(this.SelectedPortName)
                ? "Serial (auto-detect port)"
                : (this.SelectedDeviceType ?? "Auto-detect") + " on " + this.SelectedPortName;
        }
    }

    [RelayCommand]
    private async Task SetLogFolder()
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage == null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the log folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            this.LogFolder = folders[0].Path.LocalPath;
            this.StatusText = "Log folder set to " + this.LogFolder;
        }
    }

    [RelayCommand]
    private async Task OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(this.LogFolder);
            ILauncher? launcher = GetLauncher();
            if (launcher != null)
            {
                await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(this.LogFolder));
            }
        }
        catch (Exception exception)
        {
            this.StatusText = "Could not open the log folder: " + exception.Message;
        }
    }

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

    // --- Log playback (scrub through a loaded log) ------------------------------------------------
    private MonitorTab? _playbackTab;
    private DateTime _playbackStart;

    /// <summary>True while a loaded log is shown on the strip chart (the scrub bar is visible).</summary>
    [ObservableProperty] private bool _isPlayback;

    /// <summary>The loaded log's length in seconds (the scrub slider's maximum).</summary>
    [ObservableProperty] private double _playbackMaxSeconds;

    /// <summary>The scrub position in seconds from the log start (bound to the slider).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackPositionText))]
    private double _playbackSeconds;

    /// <summary>The chart's right-edge time during playback (null when live).</summary>
    [ObservableProperty] private DateTime? _playbackNow;

    public string PlaybackPositionText =>
        $"{FormatSeconds(this.PlaybackSeconds)} / {FormatSeconds(this.PlaybackMaxSeconds)}";

    partial void OnPlaybackSecondsChanged(double value)
    {
        if (this.IsPlayback)
        {
            this.PlaybackNow = _playbackStart + TimeSpan.FromSeconds(value);
        }
    }

    private static string FormatSeconds(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
        {
            seconds = 0;
        }

        int total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    [RelayCommand]
    private void ToggleMonitorPause() => this.IsMonitorPaused = !this.IsMonitorPaused;

    /// <summary>Clear the strip chart: close a loaded log if one is shown, else discard the live
    /// history and resume (Clear Log).</summary>
    [RelayCommand]
    private void ClearLog()
    {
        if (this.IsPlayback)
        {
            this.ExitPlayback();
            this.StatusText = "Playback closed.";
            return;
        }

        foreach (MonitorTab tab in this.Monitors)
        {
            tab.Clear();
        }

        this.IsMonitorPaused = false;
        this.StatusText = "Strip chart cleared.";
    }

    /// <summary>Load a saved CSV log and show it on the strip chart, paused (Load Log). The parsing is
    /// the shared <see cref="LogFileReader"/>; the end of the log is aligned to now so it sits in the
    /// window, and widening the window reveals more.</summary>
    [RelayCommand]
    private async Task LoadLog()
    {
        if (this.IsLogging)
        {
            this.StatusText = "Stop logging before loading a saved log.";
            return;
        }

        IStorageProvider? storage = GetStorageProvider();
        if (storage == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a saved log",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Log files (*.csv)") { Patterns = new[] { "*.csv" } },
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
            LoggedData data;
            using (StreamReader reader = new StreamReader(path))
            {
                data = LogFileReader.Read(reader);
            }

            if (data.Samples.Count == 0)
            {
                this.StatusText = "That log has no rows.";
                return;
            }

            this.StartPlayback(data, Path.GetFileName(path));
            string skipped = data.MalformedRows > 0 ? $", {data.MalformedRows} unreadable rows skipped" : string.Empty;
            this.StatusText =
                $"Loaded '{Path.GetFileName(path)}': {data.Samples.Count} frames, {data.Columns.Count} columns{skipped}.";
        }
        catch (Exception exception)
        {
            this.StatusText = "Unable to open that log: " + exception.Message;
        }
    }

    /// <summary>Build a Playback monitor tab with one trace per column in the loaded log (auto-ranged),
    /// select it, and set up the scrub bar over the log's time span.</summary>
    private void StartPlayback(LoggedData data, string name)
    {
        this.ExitPlayback();

        // Resolve each column to a parameter id (when a database is loaded), so a column can match a
        // monitor series by id even where the series' label differs from the parameter name.
        Dictionary<string, string> nameToId = new(StringComparer.OrdinalIgnoreCase);
        if (_database != null)
        {
            foreach (Parameter parameter in _database.ListParametersBySupportedOs(_osid))
            {
                nameToId[parameter.Name] = parameter.Id;
            }
        }

        // Pre-build each column's points and resolved id.
        List<(DateTime, double)>[] columnPoints = new List<(DateTime, double)>[data.Columns.Count];
        string?[] columnId = new string?[data.Columns.Count];
        bool[] consumed = new bool[data.Columns.Count];
        for (int i = 0; i < data.Columns.Count; i++)
        {
            List<(DateTime, double)> points = new(data.Samples.Count);
            for (int r = 0; r < data.Samples.Count; r++)
            {
                points.Add((data.Timestamps[r], data.Samples[r][i]));
            }

            columnPoints[i] = points;
            columnId[i] = nameToId.TryGetValue(data.Columns[i].Name, out string? id) ? id : null;
        }

        // Feed the existing monitor tabs (Engine, Fuelling): each series takes the matching column by
        // parameter id, or by name when no database is loaded. Series with no column in the log clear.
        foreach (MonitorTab tab in this.Monitors)
        {
            foreach (MonitorSeries series in tab.Series)
            {
                int match = -1;
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    if (consumed[i])
                    {
                        continue;
                    }

                    bool idMatch = !string.IsNullOrEmpty(series.ParameterId) && columnId[i] == series.ParameterId;
                    bool nameMatch = string.Equals(data.Columns[i].Name, series.Title, StringComparison.OrdinalIgnoreCase);
                    if (idMatch || nameMatch)
                    {
                        match = i;
                        break;
                    }
                }

                if (match >= 0)
                {
                    series.SetPoints(columnPoints[match]);
                    series.Visible = true;
                    consumed[match] = true;
                }
                else
                {
                    series.Clear();
                }
            }
        }

        // Anything the standard monitors didn't claim goes on an auto-created Log tab.
        List<int> leftover = new();
        for (int i = 0; i < data.Columns.Count; i++)
        {
            if (!consumed[i])
            {
                leftover.Add(i);
            }
        }

        if (leftover.Count > 0)
        {
            MonitorTab log = new("Log: " + name, 20);
            for (int k = 0; k < leftover.Count; k++)
            {
                int i = leftover[k];
                (double low, double high) = ColumnRange(data, i);
                MonitorSeries series = log.AddSeries(data.Columns[i].Name, data.Columns[i].Units, low, high);
                series.Visible = k < 6;   // keep a wide overflow readable; the rest are in the Traces menu
                series.SetPoints(columnPoints[i]);
            }

            this.Monitors.Add(log);
            _playbackTab = log;
        }

        // Scrub over the whole span; show a tab that actually has data.
        _playbackStart = data.Timestamps[0];
        DateTime end = data.Timestamps[data.Timestamps.Count - 1];
        this.PlaybackMaxSeconds = Math.Max(0, (end - _playbackStart).TotalSeconds);
        this.IsMonitorPaused = false;             // playback uses PlaybackNow, not the live pause
        this.IsPlayback = true;
        this.PlaybackSeconds = this.PlaybackMaxSeconds;   // start at the end (triggers PlaybackNow)
        this.PlaybackNow = _playbackStart + TimeSpan.FromSeconds(this.PlaybackSeconds);

        bool anyStandardMatched = consumed.Any(c => c);
        this.SelectedMonitor = anyStandardMatched ? this.Monitors.FirstOrDefault() : (_playbackTab ?? this.Monitors.FirstOrDefault());
    }

    /// <summary>Min..max of a loaded column (ignoring unparseable values), padded a little.</summary>
    private static (double Low, double High) ColumnRange(LoggedData data, int index)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (double[] sample in data.Samples)
        {
            double v = sample[index];
            if (double.IsNaN(v))
            {
                continue;
            }

            if (v < min) { min = v; }
            if (v > max) { max = v; }
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
        {
            return (0, 100);
        }

        if (max - min < 1e-6)
        {
            max = min + 1;
        }

        double pad = (max - min) * 0.05;
        return (min - pad, max + pad);
    }

    /// <summary>Zoom the strip chart into a dragged region (playback): narrow the window to the
    /// selection and move the play-head to its right edge.</summary>
    public void ApplyPlaybackZoom(DateTime rightEdge, double windowSeconds)
    {
        if (!this.IsPlayback || this.SelectedMonitor == null)
        {
            return;
        }

        this.SelectedMonitor.WindowSeconds = Math.Max(0.5, Math.Min(300, windowSeconds));
        double seconds = (rightEdge - _playbackStart).TotalSeconds;
        this.PlaybackSeconds = Math.Max(0, Math.Min(this.PlaybackMaxSeconds, seconds));
    }

    /// <summary>Fit the whole loaded log back in view (playback double-click).</summary>
    public void ResetPlaybackZoom()
    {
        if (!this.IsPlayback || this.SelectedMonitor == null)
        {
            return;
        }

        this.SelectedMonitor.WindowSeconds = Math.Max(2, this.PlaybackMaxSeconds);
        this.PlaybackSeconds = this.PlaybackMaxSeconds;
    }

    /// <summary>Leave playback: drop the Playback tab and return the chart to live.</summary>
    private void ExitPlayback()
    {
        // Remove the overflow Log tab and clear any loaded points fed into the standard tabs.
        if (_playbackTab != null)
        {
            this.Monitors.Remove(_playbackTab);
            _playbackTab = null;
        }

        if (this.IsPlayback)
        {
            foreach (MonitorTab tab in this.Monitors)
            {
                tab.Clear();
            }
        }

        this.IsPlayback = false;
        this.PlaybackNow = null;
        this.PlaybackMaxSeconds = 0;
        this.PlaybackSeconds = 0;

        if (this.SelectedMonitor == null || !this.Monitors.Contains(this.SelectedMonitor))
        {
            this.SelectedMonitor = this.Monitors.FirstOrDefault();
        }
    }

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

#if !ANDROID
    /// <summary>The serial ports currently present, for the Select Device dialog. Desktop only - the
    /// dialog and its Windows-only PortDiscovery/SerialPortInfo are not built for Android, which uses
    /// its own serial enumeration (see PickSerialPort) and the Android DeviceFactory.</summary>
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
#endif

#if ANDROID
    /// <summary>A device the Android picker can offer: a friendly label and the value to connect with
    /// (a Bluetooth MAC for a paired adapter, or a serial port name).</summary>
    public readonly record struct AndroidDeviceChoice(string Display, string Value);

    /// <summary>Paired Bluetooth adapters first (the usual phone path), then any USB-OTG serial ports.</summary>
    public IReadOnlyList<AndroidDeviceChoice> GetAndroidDevices()
    {
        List<AndroidDeviceChoice> devices = new();

        // Each source is enumerated defensively: Bluetooth can be off and USB-serial enumeration hits
        // /sys/class/tty, which the Android app sandbox denies (UnauthorizedAccessException). Neither
        // must be allowed to throw out of here, or the picker takes the whole app down.
        try
        {
            foreach ((string name, string addr) in BluetoothSerialPort.GetPairedDevices())
            {
                devices.Add(new AndroidDeviceChoice($"{name}  (Bluetooth)", addr));
            }
        }
        catch (Exception exception)
        {
            _logger.AddDebugMessage("Could not list paired Bluetooth devices: " + exception.Message);
        }

        try
        {
            foreach (string port in System.IO.Ports.SerialPort.GetPortNames())
            {
                devices.Add(new AndroidDeviceChoice($"{port}  (USB serial)", port));
            }
        }
        catch (Exception exception)
        {
            // Expected on a stock (non-rooted) phone: /sys/class/tty is not readable by an app. USB-OTG
            // serial adapters would need a USB-host driver anyway; Bluetooth is the supported path.
            _logger.AddDebugMessage("USB serial ports are not available on this device: " + exception.Message);
        }

        return devices;
    }

    /// <summary>Record the Android picker's choice; Connect auto-detects the adapter on it.</summary>
    public void SelectAndroidDevice(AndroidDeviceChoice choice)
    {
        this.UseJ2534 = false;
        this.SelectedPortName = choice.Value;
        this.SelectedDeviceType = null;
        this.UpdateDeviceSummary();
    }
#endif

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

    // While true, parameter-row changes don't rebuild the readings pane (used for bulk updates).
    private bool _suppressReadingRebuild;

    /// <summary>Build the Parameters-tab rows from the supported PIDs and watch them for changes.</summary>
    private void PopulateParameterRows(IEnumerable<Parameter> parameters)
    {
        this.ParameterRows.Clear();
        foreach (Parameter parameter in parameters)
        {
            ParameterRow row = new(parameter);
            row.PropertyChanged += this.OnParameterRowChanged;
            this.ParameterRows.Add(row);
        }

        this.OnParameterFilterChanged(this.ParameterFilter);
        this.RebuildReadings();
    }

    private void OnParameterRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressReadingRebuild)
        {
            return;
        }

        // Enabling/disabling a parameter or changing its units reshapes the logged set; Zoom only
        // affects the big readout.
        bool affectsLog = e.PropertyName == nameof(ParameterRow.Enabled)
            || e.PropertyName == nameof(ParameterRow.SelectedUnit);

        // Any of these is saved in a profile, so the profile now differs from its file.
        if (affectsLog || e.PropertyName == nameof(ParameterRow.Zoom))
        {
            this.IsProfileModified = true;
        }

        if (affectsLog || e.PropertyName == nameof(ParameterRow.Zoom))
        {
            this.RebuildReadings();
        }

        if (affectsLog)
        {
            this.ReconfigureIfLogging();
        }
    }

    /// <summary>When logging, push the current selection to the running loop so it reopens the session
    /// with the new columns (hot reconfigure) instead of needing a Stop/Start.</summary>
    private void ReconfigureIfLogging()
    {
        if (_controller == null || !_controller.IsRunning || !this.IsLogging)
        {
            return;
        }

        List<LogColumn> columns = this.BuildSelectedColumns();
        if (columns.Count == 0)
        {
            // Nothing selected: leave the running session alone rather than reopen with no columns.
            return;
        }

        _controller.Reconfigure(columns);
        this.StatusText = $"Updating logged parameters ({columns.Count})...";
    }

    /// <summary>Rebuild the right-hand live-value pane from the currently enabled parameters.</summary>
    private void RebuildReadings()
    {
        this.Readings.Clear();
        this.ZoomedReadings.Clear();
        _rowsById.Clear();
        foreach (ParameterRow row in this.ParameterRows)
        {
            if (!row.Enabled)
            {
                continue;
            }

            ReadingRow reading = new(row.Id, row.Name, row.SelectedUnit);
            this.Readings.Add(reading);
            _rowsById[row.Id] = reading;

            // Same instance drives the big Zoom readout, so it updates with the pane.
            if (row.Zoom)
            {
                this.ZoomedReadings.Add(reading);
            }
        }

        // Auxiliary bus values follow the PCM's, as in the log file. They are always read when the bus is
        // on, so they have rows whether or not anything is ticked. An id that is already shown (an
        // aftermarket parameter ticked on the Parameters tab) keeps its one row.
        if (_auxiliaryLogger != null && _database != null)
        {
            foreach (BusParameter parameter in _database.BusParameters
                .OrderBy(p => p.MessageId)
                .ThenBy(p => p.Id, StringComparer.Ordinal))
            {
                if (_rowsById.ContainsKey(parameter.Id))
                {
                    continue;
                }

                // The values are formatted with the selected conversion, or the first one when none is
                // selected, so the units label follows the same rule.
                Conversion? conversion = parameter.SelectedConversion ?? parameter.Conversions.FirstOrDefault();
                ReadingRow auxiliary = new(parameter.Id, parameter.Name, conversion?.Units ?? string.Empty);
                this.Readings.Add(auxiliary);
                _rowsById[parameter.Id] = auxiliary;
            }
        }

        // Query time is always the last row, as the WinForms logger shows.
        this.Readings.Add(_queryTimeRow);

        this.HasZoomedReadings = this.ZoomedReadings.Count > 0;
    }

    /// <summary>The log columns for the enabled parameters - what Start logs.</summary>
    private List<LogColumn> BuildSelectedColumns()
    {
        List<LogColumn> columns = new();
        foreach (ParameterRow row in this.ParameterRows)
        {
            if (!row.Enabled)
            {
                continue;
            }

            LogColumn? column = row.ToColumn();
            if (column != null)
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    /// <summary>
    /// Load the bundled Overview profile on connect, so a sensible default set is pre-selected (the
    /// WinForms logger starts with a default list too). A missing or unreadable default is not fatal.
    /// </summary>
    private void TryLoadDefaultProfile()
    {
        if (_database == null)
        {
            return;
        }

        try
        {
            string path = Path.Combine(PlatformPaths.ResourceDirectory, "Overview.LogProfile");
            if (!File.Exists(path))
            {
                return;
            }

            LogProfileReader reader = new(_database, _osid, _logger);
            _profile = reader.Read(path);
            this.ApplyProfileToParameterRows(_profile);
            this.ProfileName = "Overview (default)";
            _profilePath = null;
            this.IsProfileModified = false;
        }
        catch (Exception exception)
        {
            _logger.AddDebugMessage("Default profile not loaded: " + exception.Message);
        }
    }

    /// <summary>Enable the Parameters-tab rows named by a loaded profile, with its units and zoom.</summary>
    private void ApplyProfileToParameterRows(LogProfile profile)
    {
        _suppressReadingRebuild = true;
        try
        {
            Dictionary<string, ParameterRow> byId = new();
            foreach (ParameterRow row in this.ParameterRows)
            {
                row.Enabled = false;
                byId[row.Id] = row;
            }

            foreach (LogColumn column in profile.Columns)
            {
                if (byId.TryGetValue(column.Parameter.Id, out ParameterRow? row))
                {
                    row.SelectedUnit = column.Conversion.Units;
                    row.Zoom = column.Zoom;
                    row.Enabled = true;
                }
            }
        }
        finally
        {
            _suppressReadingRebuild = false;
        }

        this.RebuildReadings();

        // Opening a profile while logging swaps the whole set; reconfigure the running loop too.
        this.ReconfigureIfLogging();
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

    // --- Trouble Codes (mirrors the WinForms PcmLogger tab) --------------------------------------
    // The read/clear work lives in the shared PcmLibrary (Vehicle.ReadTroubleCodes / ClearTroubleCodes);
    // this only borrows the bus from the logging loop, calls the backend, and shows the result.

    private DiagnosticCodeDefinitions _codeDefinitions = new();

    /// <summary>What the last read found, so Clear can say what it is about to erase.</summary>
    private IReadOnlyList<DiagnosticCode> _lastReadCodes = Array.Empty<DiagnosticCode>();

    public ObservableCollection<TroubleCodeRow> TroubleCodes { get; } = new();

    [ObservableProperty]
    private string _troubleCodeStatus = "Connect, then read.";

    /// <summary>True while a read or clear is in flight, so the buttons disable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReadCodes))]
    private bool _isBusyWithCodes;

    /// <summary>The Clear button is offered only once a read has found something to clear.</summary>
    [ObservableProperty]
    private bool _canClearCodes;

    public bool CanReadCodes => !this.IsBusyWithCodes;

    /// <summary>The confirmation text the view shows before clearing (what is about to be erased).</summary>
    public string ClearConfirmationText
    {
        get
        {
            string list = string.Join(
                Environment.NewLine,
                _lastReadCodes.Select(code => code.Kind == DiagnosticCodeKind.Permanent
                    ? code + "  (permanent)"
                    : code.ToString()));

            // Said plainly: a permanent code stays until the module's own monitor passes, so a clear
            // that leaves one is working correctly.
            string note = _lastReadCodes.Any(code => code.Kind == DiagnosticCodeKind.Permanent)
                ? Environment.NewLine + Environment.NewLine +
                  "Permanent codes are cleared by the module, not by a tool, so those will remain."
                : string.Empty;

            return "Clear these codes?" + Environment.NewLine + Environment.NewLine + list + note;
        }
    }

    [RelayCommand]
    private async Task ReadCodes()
    {
        if (_vehicle == null)
        {
            this.TroubleCodeStatus = "No interface is connected.";
            return;
        }

        bool borrowed = false;
        try
        {
            this.IsBusyWithCodes = true;
            this.CanClearCodes = false;
            this.TroubleCodeStatus = "Reading...";

            // Borrow the bus from the logging loop rather than stopping it, so the strip chart and any
            // recording keep going (a few rows short), as the WinForms logger does.
            if (!await this.BorrowBus())
            {
                this.TroubleCodeStatus = "The logger did not release the interface.";
                return;
            }

            borrowed = true;

            IReadOnlyList<DiagnosticCode> codes =
                await _vehicle.ReadTroubleCodes(_codeDefinitions, CancellationToken.None);

            _lastReadCodes = codes;
            this.ShowTroubleCodes(codes);
        }
        catch (Exception exception)
        {
            this.TroubleCodeStatus = "Unable to read trouble codes.";
            _logger.AddDebugMessage(exception.ToString());
        }
        finally
        {
            if (borrowed)
            {
                _controller?.ReturnBus();
            }

            this.IsBusyWithCodes = false;
        }
    }

    /// <summary>
    /// Clear the codes. Called by the view after it confirms (the view owns the dialog, as the device
    /// and gauge dialogs are). Borrows the bus like the read does.
    /// </summary>
    public async Task ClearTroubleCodesAsync()
    {
        if (_vehicle == null)
        {
            this.TroubleCodeStatus = "No interface is connected.";
            return;
        }

        bool borrowed = false;
        try
        {
            this.IsBusyWithCodes = true;
            this.CanClearCodes = false;

            if (!await this.BorrowBus())
            {
                this.TroubleCodeStatus = "The logger did not release the interface.";
                return;
            }

            borrowed = true;

            // The list on screen is left alone: what actually went is a question only another read can
            // answer, and emptying it here would assert more than is known.
            bool accepted = await _vehicle.ClearTroubleCodes(CancellationToken.None);
            this.TroubleCodeStatus = accepted
                ? "Clear request sent. Read again to see what remains."
                : "The module did not accept the clear request.";
        }
        catch (Exception exception)
        {
            this.TroubleCodeStatus = "Unable to clear trouble codes.";
            _logger.AddDebugMessage(exception.ToString());
        }
        finally
        {
            if (borrowed)
            {
                _controller?.ReturnBus();
            }

            this.IsBusyWithCodes = false;
            this.CanClearCodes = _lastReadCodes.Count > 0;
        }
    }

    private void ShowTroubleCodes(IReadOnlyList<DiagnosticCode> codes)
    {
        this.TroubleCodes.Clear();

        foreach (DiagnosticCode code in codes)
        {
            string status = code.Status.HasValue
                ? Protocol.DescribeTroubleCodeStatus(code.Status.Value)
                : code.Kind.ToString();

            this.TroubleCodes.Add(new TroubleCodeRow(
                code.Name,
                status,
                code.Description ?? "No description available"));
        }

        this.TroubleCodeStatus = codes.Count == 0 ? "No trouble codes." : $"{codes.Count} trouble codes.";

        // Offered whenever anything was found, including a list of nothing but permanent codes - letting
        // the request run and reporting what came back beats a greyed-out button.
        this.CanClearCodes = codes.Count > 0;
    }

    /// <summary>
    /// Borrow the bus from the logging loop (if one is running) so a read/clear is safe. The loop keeps
    /// the session and recording open and resumes once <see cref="LoggingController.ReturnBus"/> is
    /// called. Returns false only if the loop would not yield in time (caller then leaves it alone).
    /// </summary>
    private async Task<bool> BorrowBus()
    {
        if (_controller == null)
        {
            return true;
        }

        return await _controller.BorrowBusAsync(TimeSpan.FromSeconds(5));
    }

    public MainWindowViewModel()
    {
        // Library messages can arrive on background threads; marshal the status update to the UI.
        _logger = new StatusLogger(
            onUserMessage: message => Dispatcher.UIThread.Post(() => this.StatusText = message),
            onDebugMessage: message => Dispatcher.UIThread.Post(() => this.AppendDebug(message)));

        // Trouble-code descriptions sit in the resource directory (beside the exe on desktop; a copied
        // asset folder on Android), as WinForms/PcmLogger does.
        _codeDefinitions = DiagnosticCodeDefinitions.Load(PlatformPaths.ResourceDirectory, _logger);

        _queryTimeRow.Value = "--";
        this.LoadRecentProfiles();

        // Backing fields, so loading the saved choice doesn't save it straight back or try to open it
        // before there is a PCM connection to go with it.
        _auxiliaryBusEnabled = _settings.AuxiliaryBusEnabled;
        _auxiliaryPortName = string.IsNullOrEmpty(_settings.AuxiliaryPort) ? null : _settings.AuxiliaryPort;
        if (_auxiliaryPortName != null)
        {
            this.AuxiliaryPorts.Add(_auxiliaryPortName);
        }

        this.UpdateDeviceSummary();
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

    /// <summary>The Connect/Disconnect button: connect when idle, release the interface when connected.</summary>
    [RelayCommand]
    private async Task ToggleConnection()
    {
        if (this.IsConnected)
        {
            await this.Disconnect();
        }
        else
        {
            await this.Connect();
        }
    }

    /// <summary>Release the interface and stop any live logging, back to the disconnected state.</summary>
    private async Task Disconnect()
    {
        if (_controller != null)
        {
            await _controller.StopAsync();
            _controller.RowAvailable -= OnRowAvailable;
            _controller = null;
        }

        this.IsLogging = false;
        this.IsRecording = false;

        // Release the analyzer with the PCM interface; it is opened again on the next connect.
        await _auxiliaryGate.WaitAsync();
        try
        {
            await this.StopAuxiliaryBusAsync();
            this.AuxiliaryStatus = this.AuxiliaryBusEnabled ? "Will start when the PCM is connected." : "Off.";
        }
        finally
        {
            _auxiliaryGate.Release();
        }

        try
        {
            _device?.Dispose();
        }
        catch
        {
            // Best effort: the port is being released regardless.
        }

        _device = null;
        _vehicle = null;
        _database = null;
        _profile = null;
        _profilePath = null;
        this.IsProfileModified = false;
        this.AvailableParameters.Clear();
        this.ParameterRows.Clear();
        this.Readings.Clear();
        _rowsById.Clear();
        this.IsConnected = false;
        this.StatusText = "Disconnected.";
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
#if !ANDROID
            // J2534 pass-through drivers are Windows-only; Android has no J2534 path, so it compiles
            // down to the serial branch below.
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
#endif
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

            _database = new ParameterDatabase(PlatformPaths.ResourceDirectory);
            _database.LoadDatabase();

            List<Parameter> supported = _database.ListParametersBySupportedOs(_osid).OrderBy(p => p.Name).ToList();

            this.AvailableParameters.Clear();
            foreach (Parameter parameter in supported)
            {
                string units = parameter.Conversions.FirstOrDefault()?.Units ?? string.Empty;
                this.AvailableParameters.Add(new ParameterOption(parameter.Id, parameter.Name, units));
            }

            this.PopulateParameterRows(supported);
            this.TryLoadDefaultProfile();

            this.IsConnected = true;
            this.StatusText = $"Connected: {_device.GetDeviceType()}, OSID {_osid}.";

            // Bring the auxiliary bus up first, so a log that starts by itself already has its columns.
            // A failure there is reported on the Configuration tab and does not stop the connection.
            if (this.AuxiliaryBusEnabled)
            {
                this.RefreshAuxiliaryPorts();
                await this.ApplyAuxiliaryBusAsync();
            }

            // Auto start logging if asked and parameters are already selected (the Configuration option).
            if (this.AutoStartLogging && !this.IsLogging && this.BuildSelectedColumns().Count > 0)
            {
                this.Start();
            }
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

        if (!await this.ConfirmDiscardChangesAsync())
        {
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

            this.LoadProfileFromPath(files[0].Path.LocalPath);
        }
        catch (Exception exception)
        {
            this.StatusText = "Open profile failed: " + exception.Message;
        }
    }

    /// <summary>Open a profile from the recent list. A file that has since been moved or deleted is
    /// dropped from the list instead of failing.</summary>
    [RelayCommand]
    private async Task OpenRecentProfile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!this.IsConnected || _database == null)
        {
            this.StatusText = "Connect to a PCM before opening a profile.";
            return;
        }

        if (!File.Exists(path))
        {
            this.RemoveRecentProfile(path);
            this.StatusText = $"'{Path.GetFileName(path)}' no longer exists and was removed from the recent list.";
            return;
        }

        if (!await this.ConfirmDiscardChangesAsync())
        {
            return;
        }

        this.LoadProfileFromPath(path);
    }

    /// <summary>Read a .LogProfile and apply it to the Parameters tab.</summary>
    private void LoadProfileFromPath(string path)
    {
        if (_database == null)
        {
            return;
        }

        try
        {
            LogProfileReader reader = new(_database, _osid, _logger);
            _profile = reader.Read(path);

            // Apply the profile to the Parameters tab: enable the matching rows, set their units and
            // zoom. The rows are the source of truth, so the live-value pane and Start follow from them.
            this.ApplyProfileToParameterRows(_profile);

            this.ProfileName = System.IO.Path.GetFileName(path);
            _profilePath = path;
            this.IsProfileModified = false;
            this.AddRecentProfile(path);
            this.StatusText = $"Loaded profile '{this.ProfileName}' ({this.Readings.Count} parameters).";

            // Auto start logging if asked and already connected (the Configuration tab option).
            if (this.AutoStartLogging && this.IsConnected && !this.IsLogging)
            {
                this.Start();
            }
        }
        catch (Exception exception)
        {
            this.StatusText = "Open profile failed: " + exception.Message;
        }
    }

    // Recently opened or saved profiles, newest first, kept across runs in the logger's settings file.
    private readonly LoggerSettings _settings = LoggerSettings.Load();

    public ObservableCollection<RecentProfileItem> RecentProfiles { get; } = new();

    private void LoadRecentProfiles()
    {
        this.RecentProfiles.Clear();
        foreach (string path in _settings.RecentProfiles)
        {
            this.RecentProfiles.Add(new RecentProfileItem(path));
        }
    }

    private void AddRecentProfile(string path)
    {
        _settings.AddRecentProfile(path);
        _settings.Save();
        this.LoadRecentProfiles();
    }

    private void RemoveRecentProfile(string path)
    {
        _settings.RemoveRecentProfile(path);
        _settings.Save();
        this.LoadRecentProfiles();
    }

    /// <summary>If the profile has unsaved changes, ask before they are thrown away. True means go on.</summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!this.IsProfileModified || this.ConfirmAsync == null)
        {
            return true;
        }

        return await this.ConfirmAsync(
            $"'{this.ProfileName}' has unsaved changes. Discard them?");
    }

    /// <summary>Start an empty profile: every parameter off, ready to be picked on the Parameters tab.</summary>
    [RelayCommand]
    private async Task NewProfile()
    {
        if (!this.IsConnected || _database == null)
        {
            this.StatusText = "Connect to a PCM before creating a profile.";
            return;
        }

        if (!await this.ConfirmDiscardChangesAsync())
        {
            return;
        }

        _profile = new LogProfile();
        this.ApplyProfileToParameterRows(_profile);
        this.ProfileName = "(new profile)";
        _profilePath = null;
        this.IsProfileModified = false;
        this.StatusText = "New profile. Enable parameters on the Parameters tab.";
    }

    /// <summary>Save the current selection to the file this profile came from. A new profile, or the
    /// bundled default (which must not be overwritten), asks where to save instead.</summary>
    [RelayCommand]
    private async Task SaveProfile()
    {
        if (_profilePath == null)
        {
            await this.SaveProfileAs();
            return;
        }

        List<LogColumn> columns = this.BuildSelectedColumns();
        if (columns.Count == 0)
        {
            this.StatusText = "Enable parameters on the Parameters tab before saving a profile.";
            return;
        }

        this.WriteProfile(columns, _profilePath);
    }

    private void WriteProfile(List<LogColumn> columns, string path)
    {
        try
        {
            LogProfile profile = new();
            foreach (LogColumn column in columns)
            {
                profile.AddColumn(column);
            }

            LogProfileWriter.Write(profile, path);
            _profile = profile;
            _profilePath = path;
            this.ProfileName = Path.GetFileName(path);
            this.IsProfileModified = false;
            this.AddRecentProfile(path);
            this.StatusText = $"Saved profile '{this.ProfileName}' ({columns.Count} parameters).";
        }
        catch (Exception exception)
        {
            this.StatusText = "Save profile failed: " + exception.Message;
        }
    }

    /// <summary>Save the current Parameters-tab selection (enabled rows, with units and zoom) as a
    /// .LogProfile under a name the user picks, using the shared <see cref="LogProfileWriter"/>.</summary>
    [RelayCommand]
    private async Task SaveProfileAs()
    {
        List<LogColumn> columns = this.BuildSelectedColumns();
        if (columns.Count == 0)
        {
            this.StatusText = "Enable parameters on the Parameters tab before saving a profile.";
            return;
        }

        IStorageProvider? storage = GetStorageProvider();
        if (storage == null)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save log profile",
            DefaultExtension = "LogProfile",
            SuggestedFileName = "Custom.LogProfile",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Log profiles (*.LogProfile)") { Patterns = new[] { "*.LogProfile" } },
            },
        });

        if (file == null)
        {
            return;
        }

        this.WriteProfile(columns, file.Path.LocalPath);
    }

    /// <summary>The Start/Stop Logging button: start when idle, stop when logging.</summary>
    [RelayCommand]
    private async Task ToggleLogging()
    {
        if (this.IsLogging)
        {
            await this.Stop();
        }
        else
        {
            this.Start();
        }
    }

    [RelayCommand]
    private void Start()
    {
        if (_vehicle == null || this.IsLogging)
        {
            this.StatusText = "Connect first.";
            return;
        }

        List<LogColumn> columns = this.BuildSelectedColumns();
        if (columns.Count == 0)
        {
            this.StatusText = "Enable parameters on the Parameters tab (or open a profile) before starting.";
            return;
        }

        this.ExitPlayback();   // leave any loaded-log playback before going live

        _lastRowTime = default;
        _queryTimeRow.Value = "--";

        // Going live: resume the chart (a previous stop left it frozen for inspection).
        this.IsMonitorPaused = false;

        _controller = new LoggingController(_vehicle, _osid, _logger);

        // Set before Start so the first session already includes the auxiliary columns, if the bus is on.
        _controller.SetAuxiliaryBus(_auxiliaryLogger);
        _controller.RowAvailable += OnRowAvailable;
        _controller.Stopped += () => Dispatcher.UIThread.Post(() =>
        {
            this.IsLogging = false;
            // Freeze the strip chart where it stopped, so the last data can be inspected.
            this.IsMonitorPaused = true;
        });
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
        _controller.Reconfigured += count =>
            Dispatcher.UIThread.Post(() => this.StatusText = $"Logging {count} parameters.");

        _controller.Start(columns);
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
            Directory.CreateDirectory(this.LogFolder);
            string path = Path.Combine(this.LogFolder, "PcmLog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
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
        // Freeze the strip chart where it stopped, so the last data can be inspected.
        this.IsMonitorPaused = true;
        this.StatusText = this.IsConnected ? "Stopped." : "Not connected.";
    }

    // ---- Auxiliary CAN bus (SeeedStudio USB-CAN analyzer) ------------------------------------------------
    // Reads aftermarket CAN sensors (an AEM wideband and the like) from a second interface while the PCM
    // is logged, as the WinForms logger does. The analyzer is a separate device from the OBD2 interface,
    // so it has its own port. Desktop only: it needs serial-port enumeration, which Android doesn't have
    // here. A failure to open it is reported and ignored, because a second bus that won't open is no
    // reason to stop logging the PCM.

#if ANDROID
    public bool AuxiliaryBusSupported => false;
#else
    public bool AuxiliaryBusSupported => true;
#endif

    [ObservableProperty]
    private bool _auxiliaryBusEnabled;

    [ObservableProperty]
    private string? _auxiliaryPortName;

    [ObservableProperty]
    private string _auxiliaryStatus = "Off.";

    public ObservableCollection<string> AuxiliaryPorts { get; } = new();

    private AuxiliaryBusLogger? _auxiliaryLogger;
    private Device? _auxiliaryDevice;
    private bool _refreshingAuxiliaryPorts;

    // Opening and closing the analyzer must not overlap (a toggle followed quickly by a port change).
    private readonly SemaphoreSlim _auxiliaryGate = new(1, 1);

    partial void OnAuxiliaryBusEnabledChanged(bool value)
    {
        _settings.AuxiliaryBusEnabled = value;
        _settings.Save();

        if (value)
        {
            this.RefreshAuxiliaryPorts();
        }

        _ = this.ApplyAuxiliaryBusAsync();
    }

    partial void OnAuxiliaryPortNameChanged(string? value)
    {
        // The list being rebuilt clears the ComboBox selection; that is not the user's choice.
        if (_refreshingAuxiliaryPorts)
        {
            return;
        }

        _settings.AuxiliaryPort = value ?? string.Empty;
        _settings.Save();

        if (this.AuxiliaryBusEnabled)
        {
            _ = this.ApplyAuxiliaryBusAsync();
        }
    }

    /// <summary>List the serial ports, keeping the saved choice selected even if it isn't plugged in.</summary>
    [RelayCommand]
    private void RefreshAuxiliaryPorts()
    {
#if !ANDROID
        string? current = this.AuxiliaryPortName;
        _refreshingAuxiliaryPorts = true;
        try
        {
            this.AuxiliaryPorts.Clear();
            foreach (SerialPortInfo port in PortDiscovery.GetPorts(_logger))
            {
                if (!string.IsNullOrEmpty(port.PortName) && !this.AuxiliaryPorts.Contains(port.PortName))
                {
                    this.AuxiliaryPorts.Add(port.PortName);
                }
            }

            if (!string.IsNullOrEmpty(current) && !this.AuxiliaryPorts.Contains(current))
            {
                this.AuxiliaryPorts.Add(current);
            }

            this.AuxiliaryPortName = current;
        }
        catch (Exception exception)
        {
            _logger.AddDebugMessage("Unable to list serial ports for the auxiliary bus: " + exception.Message);
        }
        finally
        {
            _refreshingAuxiliaryPorts = false;
        }
#endif
    }

    /// <summary>Open the analyzer and start reading it, or stop, to match the settings and connection.</summary>
    private async Task ApplyAuxiliaryBusAsync()
    {
#if !ANDROID
        await _auxiliaryGate.WaitAsync();
        try
        {
            await this.StopAuxiliaryBusAsync();

            if (!this.AuxiliaryBusEnabled)
            {
                this.AuxiliaryStatus = "Off.";
                return;
            }

            ParameterDatabase? database = _database;
            if (database == null)
            {
                this.AuxiliaryStatus = "Will start when the PCM is connected.";
                return;
            }

            string? port = this.AuxiliaryPortName;
            if (string.IsNullOrEmpty(port))
            {
                this.AuxiliaryStatus = "Choose the analyzer's port.";
                return;
            }

            if (string.Equals(port, this.SelectedPortName, StringComparison.OrdinalIgnoreCase))
            {
                this.AuxiliaryStatus = $"{port} is the OBD2 interface's port. The analyzer needs its own.";
                return;
            }

            Device? device = null;
            try
            {
                this.AuxiliaryStatus = $"Opening {port}...";

                device = await Task.Run(() =>
                    DeviceFactory.CreateSerialDevice(port, UsbCanAnalyzerDevice.DeviceType, _logger));

                if (device == null || !await device.Initialize())
                {
                    device?.Dispose();
                    this.AuxiliaryStatus = $"Unable to open the analyzer on {port}.";
                    return;
                }

                AuxiliaryBusLogger busLogger = new(database, _logger);
                await busLogger.Start(device, BusProtocol.Can500k);
                if (!busLogger.IsRunning)
                {
                    device.Dispose();
                    this.AuxiliaryStatus = $"The analyzer on {port} could not start reading CAN.";
                    return;
                }

                _auxiliaryDevice = device;
                _auxiliaryLogger = busLogger;
                _controller?.SetAuxiliaryBus(busLogger);
                this.RebuildReadings();
                this.AuxiliaryStatus = $"Reading CAN 500k on {port}.";
            }
            catch (Exception exception)
            {
                // Opening a port that is absent or held by something else throws rather than returning
                // false. Logging the PCM carries on without the second bus.
                device?.Dispose();
                this.AuxiliaryStatus = $"Unable to open {port}: {exception.Message}";
                _logger.AddDebugMessage(exception.ToString());
            }
        }
        finally
        {
            _auxiliaryGate.Release();
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Stop reading the analyzer and release it. The read loop must end before the device is
    /// disposed, or the driver can still be servicing a read when the port closes.</summary>
    private async Task StopAuxiliaryBusAsync()
    {
        _controller?.SetAuxiliaryBus(null);

        AuxiliaryBusLogger? busLogger = _auxiliaryLogger;
        Device? device = _auxiliaryDevice;
        _auxiliaryLogger = null;
        _auxiliaryDevice = null;

        if (busLogger != null)
        {
            try
            {
                await busLogger.Stop();
            }
            catch (Exception exception)
            {
                _logger.AddDebugMessage("Auxiliary bus did not stop cleanly: " + exception.Message);
            }

            busLogger.Dispose();
        }

        try
        {
            device?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.AddDebugMessage("Unable to close the auxiliary device: " + exception.Message);
        }

        if (busLogger != null)
        {
            this.RebuildReadings();
        }
    }

    /// <summary>Append a debug line to the Debug tab, keeping the list bounded.</summary>
    private void AppendDebug(string message)
    {
        this.DebugMessages.Add(message);
        const int max = 2000;
        while (this.DebugMessages.Count > max)
        {
            this.DebugMessages.RemoveAt(0);
        }
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

            // Query time: the ms since the previous row (the WinForms "Query time" readout).
            DateTime now = DateTime.Now;
            if (_lastRowTime != default)
            {
                _queryTimeRow.Value = (now - _lastRowTime).TotalMilliseconds.ToString("0.00");
            }

            _lastRowTime = now;
        });
    }

#if ANDROID
    // Android has no PortDiscovery/SerialPortInfo (those are Windows-only); enumerate with
    // System.IO.Ports directly. Prefer a USB/ACM port (OBDX and similar) over anything else.
    private static string? PickSerialPort()
    {
        string[] ports;
        try
        {
            ports = System.IO.Ports.SerialPort.GetPortNames();
        }
        catch (Exception)
        {
            // /sys/class/tty is not readable by a stock Android app; there is no USB-serial port to pick.
            return null;
        }

        if (ports.Length == 0)
        {
            return null;
        }

        string? preferred = ports.FirstOrDefault(p =>
            p.IndexOf("ACM", StringComparison.OrdinalIgnoreCase) >= 0 ||
            p.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);

        return preferred ?? ports[0];
    }
#else
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
#endif

    private static readonly ILogger NullLogger = new StatusLogger(_ => { }, _ => { });

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.StorageProvider;
        }

        return null;
    }

    private static ILauncher? GetLauncher()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Launcher;
        }

        return null;
    }
}
