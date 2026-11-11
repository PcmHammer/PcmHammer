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

    // parameter id -> its row, so each reading updates in place rather than rebuilding the list.
    private readonly Dictionary<string, ReadingRow> _rowsById = new();

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    private string _profileName = "(no profile)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLogging;

    public ObservableCollection<ReadingRow> Readings { get; } = new();

    public MainWindowViewModel()
    {
        // Library messages can arrive on background threads; marshal the status update to the UI.
        _logger = new StatusLogger(
            onUserMessage: message => Dispatcher.UIThread.Post(() => this.StatusText = message),
            onDebugMessage: _ => { });
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

            this.Readings.Clear();
            _rowsById.Clear();
            foreach (LogColumn column in _profile.Columns)
            {
                ReadingRow row = new(column.Parameter.Id, column.Parameter.Name, column.Conversion.Units);
                this.Readings.Add(row);
                _rowsById[column.Parameter.Id] = row;
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
