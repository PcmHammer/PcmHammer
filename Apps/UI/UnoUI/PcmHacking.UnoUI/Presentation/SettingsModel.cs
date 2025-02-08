using System.ComponentModel;
using System.Runtime.CompilerServices;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive;
using Windows.Networking;
using Windows.Storage.Pickers;
//using Windows.System;
using Microsoft.UI.Dispatching;

namespace PcmHacking.UnoUI.Presentation;

public record CurrentSettings(string DeviceCategory, string Obd2SerialPortName, string Obd2SerialDeviceName, string J2534DeviceName, bool CanEnabled, string CanPort);

public partial record SettingsModel
{
    private readonly PcmHacking.ILogger progressLogger;
    private readonly ISettingsService settingsService;
    private readonly ConnectionService vehicleService;
    private readonly DispatcherQueue dispatcherQueue;

    public string Title { get { return "Settings"; } }

    public SettingsModel(
        PcmHacking.ILogger progressLogger, 
        ISettingsService settingsService,
        ConnectionService vehicleService,
        DispatcherQueue dispatcherQueue)
    {
        this.progressLogger = progressLogger;
        this.settingsService = settingsService;
        this.vehicleService = vehicleService;
        this.dispatcherQueue = dispatcherQueue;
    }

    public IListFeed<string> SerialDeviceTypes => ListFeed<string>.Async(ct => this.GetObd2SerialDeviceTypes(ct)).Selection(SelectedObd2SerialDeviceType);
    public IListFeed<string> Obd2Ports => ListFeed.Async<string>(ct => this.GetPortNames(ct)).Selection(SelectedObd2Port);
    public IListFeed<string> CanPorts => ListFeed.Async<string>(ct => this.GetPortNames(ct)).Selection(SelectedCanPort);

    public IState<bool> UseSerialDevice => State<bool>
        .Async(this, ct => this.AreEqual("Serial", settingsService.GetObd2DeviceCategory(ct).Result))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<bool> UseJ2534Device => State<bool>
        .Async(this, ct => this.AreEqual("J2534", settingsService.GetObd2DeviceCategory(ct).Result));

    public IState<bool> UseCanDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(settingsService.IsCanEnabled(ct).Result))
        .ForEach(ConnectionSettingsChanged);
    public IState<bool> DontUseCanDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(!settingsService.IsCanEnabled(ct).Result));

    public IState<string> SelectedObd2Port => State<string>
        .Async(this, ct => settingsService.GetObd2SerialPortName(ct))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<string> SelectedCanPort => State<string>
        .Async(this, ct => settingsService.GetCanSerialPortName(ct))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<string> SelectedObd2SerialDeviceType => State<string>
        .Async(this, ct => settingsService.GetObd2SerialDeviceName(ct))
        .ForEach(this.ConnectionSettingsChanged);

    public IState<string> DataLogFolder => State<string>
        .Async(this, ct => settingsService.GetDataLogFolder(ct));

    private ValueTask<IImmutableList<string>> GetPortNames(CancellationToken ct)
    {
#if ANDROID || IOS || MACOS
        IImmutableList<string> result = ImmutableList.CreateRange(new string[0]);
        return ValueTask.FromResult(result);
#endif
#if DOES_NOT_WORK
        IEnumerable<SerialPortInfo> ports = PortDiscovery.GetPorts(this.progressLogger);
        IImmutableList<string> result = ImmutableList.CreateRange(ports.Select(port => $"${port.PortName} - ${port.DeviceID}"));
        return ValueTask.FromResult(result);
#endif
        // WORKS_BUT_NO_DRIVER_NAMES
        // TODO: Use Windows Management API to get the device driver names.
        string[] portNames = System.IO.Ports.SerialPort.GetPortNames();
        IList<string> portList = new List<string>(portNames);
        portList.Add(MockPort.PortName);
        IImmutableList<string> result = ImmutableList.CreateRange(portList);
        return ValueTask.FromResult(result);
    }

    private ValueTask<IImmutableList<string>> GetObd2SerialDeviceTypes(CancellationToken ct)
    {
        string[] deviceTypes = new string[] { MockDevice.DeviceType, OBDXProDevice.DeviceType, ElmDevice.DeviceType, AvtDevice.DeviceType };
        IImmutableList<string> result = ImmutableList.CreateRange(deviceTypes);
        return ValueTask.FromResult(result);
    }

    private ValueTask<bool> AreEqual(string value1, string value2)
    {
        return ValueTask.FromResult(value1 == value2);
    }

    private async ValueTask ConnectionSettingsChanged<T>(T newValue, CancellationToken ct)
    {
        CurrentSettings currentSettings = new CurrentSettings(
            await this.UseSerialDevice.Value() ? "Serial" : "J2534",
            await this.SelectedObd2Port.Value() ?? "",
            await this.SelectedObd2SerialDeviceType.Value() ?? "",
            "", // TODO: J2534 device name
            await this.UseCanDevice.Value(),
            await this.SelectedCanPort.Value() ?? "");

        if (await this.vehicleService.TryConnect(currentSettings))
        {
            this.settingsService.SaveConnectionSettings(currentSettings);
        }
    }

    public Task OpenLogFolderPicker()
    {
        dispatcherQueue.TryEnqueue(async () =>
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();

            // https://platform.uno/docs/articles/platform-specific-csharp.html
            // https://platform.uno/docs/articles/features/windows-storage-pickers.html
            // These lines are needed to make the folder picker work on Windows.
#if WINDOWS10_0_26100_0_OR_GREATER
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StaticMainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
#endif

            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            try
            {
                Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    settingsService.SetDataLogFolder(folder.Path);
                    await DataLogFolder.SetAsync(folder.Path);
                }
            }
            catch (Exception exception)
            {
                progressLogger.AddUserMessage(exception.Message);
            }
        });
        return Task.CompletedTask;
    }

    public Task OpenLogFolder()
    {
        dispatcherQueue.TryEnqueue(async () =>
        {
            string folder = await settingsService.GetDataLogFolder(CancellationToken.None);
            if (string.IsNullOrEmpty(folder))
            {
                progressLogger.AddUserMessage("No folder has been configured.");
                return;
            }

#if WINDOWS10_0_26100_0_OR_GREATER
            // TODO: Use a cross-platform API to open the folder.
            Windows.Storage.StorageFolder storageFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folder);
            await Windows.System.Launcher.LaunchFolderAsync(storageFolder);
#endif
        });
        return Task.CompletedTask;
    }
}
