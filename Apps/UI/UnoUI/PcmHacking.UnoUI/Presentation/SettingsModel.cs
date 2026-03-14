using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PcmHacking.UnoUI.Presentation;

public record CurrentSettings(
    string DeviceCategory, 
    string Obd2SerialPortName, 
    string Obd2SerialDeviceName,
    string J2534DeviceName,
    bool CanEnabled, 
    string CanPort);

public partial record SettingsModel
{
    private readonly LoggerAdapter progressLogger;
    private readonly ISettingsService settingsService;
    private readonly IConnectionService connectionService;
    private readonly DispatcherQueue dispatcherQueue;

    public string Title { get { return "Settings"; } }

    public SettingsModel(
        LoggerAdapter progressLogger, 
        ISettingsService settingsService,
        IConnectionService connectionService,
        DispatcherQueue dispatcherQueue)
    {
        this.progressLogger = progressLogger;
        this.settingsService = settingsService;
        this.connectionService = connectionService;
        this.dispatcherQueue = dispatcherQueue;
    }

    public IListFeed<string> SerialDeviceTypes => ListFeed<string>.Async(ct => this.GetObd2SerialDeviceTypes(ct)).Selection(SelectedObd2DeviceType);
    public IListFeed<string> Obd2Ports => ListFeed.Async<string>(ct => this.GetPortNames(ct)).Selection(SelectedObd2Port);
    public IListFeed<string> CanPorts => ListFeed.Async<string>(ct => this.GetPortNames(ct)).Selection(SelectedCanPort);
    public IListFeed<string> JDevices => ListFeed.Async<string>(ct => this.GetJDevices(ct)).Selection(SelectedJDevice);

    public IState<bool> UseSerialDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(settingsService.IsSerialDevice()))
        .ForEach(this.SerialRadioButtonChanged);

    public IState<bool> UseJ2534Device => State<bool>
        .Async(this, ct => ValueTask.FromResult(!settingsService.IsSerialDevice()));
    public IState<bool> UseCanDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(settingsService.IsCanEnabled()))
        .ForEach(ConnectionSettingsChanged);
    public IState<bool> DontUseCanDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(!settingsService.IsCanEnabled()));

    public IState<string> SelectedObd2Port => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetObd2PortName()))
        .ForEach(this.ConnectionSettingsChanged); 
    public IState<string> SelectedJDevice => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetJ2534DeviceName()))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<string> SelectedCanPort => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetCanSerialPortName()))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<string> SelectedObd2DeviceType => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetObd2DeviceName()))
        .ForEach(this.ConnectionSettingsChanged);

    public IState<string> DataLogFolder => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetDataLogFolder()));

    public IState<bool> Enable4x => State<bool>
        .Async(this, ct => ValueTask.FromResult(settingsService.Is4xReadWriteEnabled()))
        .ForEach(this.Enable4xReadWriteChanged);

    private ValueTask<IImmutableList<string>> GetPortNames(CancellationToken ct)
    {
        IList<string> portList = [];
#if ANDROID || IOS || MACOS
        IImmutableList<string> result = ImmutableList.CreateRange(new string[0]);
        return ValueTask.FromResult(result);
#else
        // WORKS_BUT_NO_DRIVER_NAMES
        // TODO: Use Windows Management API to get the device driver names.
        string[] portNames = System.IO.Ports.SerialPort.GetPortNames();
        portList = new List<string>(portNames);
        portList.Add(MockPort.PortName);
        IImmutableList<string> result = ImmutableList.CreateRange(portList);
        return ValueTask.FromResult(result);
#endif
    }

    private ValueTask<IImmutableList<string>> GetJDevices(CancellationToken ct)
    {
        List<string> jDevices = [];
#if WINDOWS
            foreach (J2534DotNet.J2534Device device in J2534DeviceFinder.FindInstalledJ2534DLLs(this.progressLogger))
            {
                jDevices.Add(device.Name);
            }
#endif
            IImmutableList<string> res = ImmutableList.CreateRange(jDevices);
            return ValueTask.FromResult(res);

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

    private async ValueTask SerialRadioButtonChanged(bool newValue, CancellationToken ct)
    {
        await UseSerialDevice.SetAsync(newValue);
        await SelectedObd2DeviceType.SetAsync("");
        await SelectedObd2Port.SetAsync(newValue ? "" : "J2534");
        await SelectedJDevice.SetAsync("");
    }

    private async ValueTask ConnectionSettingsChanged<T>(T newValue, CancellationToken ct)
    {
        CurrentSettings currentSettings = new CurrentSettings(
            await this.UseSerialDevice.Value() ? "Serial" : "J2534",
            await this.SelectedObd2Port.Value() ?? "",
            await this.UseSerialDevice.Value() ? await this.SelectedObd2DeviceType.Value() : await this.SelectedJDevice.Value() ?? "",
            await this.SelectedJDevice.Value() ?? "",
            await this.UseCanDevice.Value(),
            await this.SelectedCanPort.Value() ?? "");

        if (await this.connectionService.TryConnect(currentSettings))
        {
            this.settingsService.SaveConnectionSettings(currentSettings);
        }
    }

    private ValueTask Enable4xReadWriteChanged(bool newValue, CancellationToken ct)
    {
        settingsService.Is4xReadWriteEnabled(newValue);
        return ValueTask.CompletedTask;
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
        dispatcherQueue.TryEnqueue(
#if WINDOWS10_0_26100_0_OR_GREATER
            async 
#endif
            () =>
        {
            string folder = settingsService.GetDataLogFolder();
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
