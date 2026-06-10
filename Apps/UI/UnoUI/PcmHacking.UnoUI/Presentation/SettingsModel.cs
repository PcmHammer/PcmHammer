// SPDX-License-Identifier: GPL-3.0-only
using InTheHand.Net.Sockets;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using SkiaSharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Uno.Extensions.Reactive;
using Windows.Networking;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record CurrentSettings(
    string DeviceCategory, 
    string DeviceNameOrPort, 
    bool CanEnabled, 
    string CanPort);

public class SerialPortListing
{
    public string? DisplayName { get; set; }
    public string? PortName { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is SerialPortListing listing &&
               PortName == listing.PortName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(PortName);
    }

    public override string ToString()
    {
        return DisplayName ?? string.Empty;
    }
}

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

    private CancellationTokenSource? deviceConnectionCancelSource = new();
    private Signal _deviceRefreshSignal = new();
    public IListFeed<string> DeviceCategories => ListFeed<string>.Async(ct => this.GetDeviceCategories(ct)).Selection(SelectedDeviceType);
    public IListFeed<SerialPortListing> BTDevices => ListFeed<SerialPortListing>.Async(ct => this.GetBluetoothDevices(ct), _deviceRefreshSignal).Selection(SelectedBluetoothDevice);
    public IListFeed<string> JDevices => ListFeed<string>.Async(ct => this.GetJDevices(ct), _deviceRefreshSignal).Selection(SelectedJDevice);
    public IListFeed<SerialPortListing> Obd2Ports => ListFeed.Async(ct => this.GetPortNames(ct), _deviceRefreshSignal).Selection(SelectedObd2Port);
    public IListFeed<SerialPortListing> CanPorts => ListFeed.Async(ct => this.GetPortNames(ct), _deviceRefreshSignal).Selection(SelectedCanPort);
    
    public IState<string> SelectedDeviceType => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetObd2DeviceCategory()))
        .ForEach(DeviceCategoryChanged);

    public IState<bool> UseSerialDevice => State<bool>
        .Async(this, ct => this.AreEqual(DeviceConstants.DeviceCategorySerial, settingsService.GetObd2DeviceCategory()));
    public IState<bool> UseJ2534Device => State<bool>
        .Async(this, ct => this.AreEqual(DeviceConstants.DeviceCategoryJ2534, settingsService.GetObd2DeviceCategory()));
    public IState<bool> UseBTDevice => State<bool>
        .Async(this, ct => this.AreEqual(DeviceConstants.DeviceCategoryBT, settingsService.GetObd2DeviceCategory()));

    public IState<bool> UseCanDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(settingsService.IsCanEnabled()))
        .ForEach(ConnectionSettingsChanged);
    public IState<bool> DontUseCanDevice => State<bool>
        .Async(this, ct => ValueTask.FromResult(!settingsService.IsCanEnabled()));

    public IState<SerialPortListing> SelectedObd2Port => State<SerialPortListing>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetObd2SerialPortName()))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<SerialPortListing> SelectedCanPort => State<SerialPortListing>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetCanSerialPortName()))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<string> SelectedJDevice => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetJ2534DeviceName()))
        .ForEach(this.ConnectionSettingsChanged);
    public IState<SerialPortListing> SelectedBluetoothDevice => State<SerialPortListing>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetBluetoothDeviceAddress()))
        .ForEach(this.ConnectionSettingsChanged);

    public IState<string> DataLogFolder => State<string>
        .Async(this, ct => ValueTask.FromResult(settingsService.GetDataLogFolder()));

    public IState<bool> Enable4x => State<bool>
        .Async(this, ct => ValueTask.FromResult(settingsService.Is4xReadWriteEnabled()))
        .ForEach(this.Enable4xReadWriteChanged);

    public bool IsWindowsPlatform 
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    private ValueTask<IImmutableList<string>> GetDeviceCategories(CancellationToken ct)
    {
        List<string> deviceCategories = [];
        deviceCategories.Add(DeviceConstants.DeviceCategorySerial);
#if WINDOWS
        deviceCategories.Add(DeviceConstants.DeviceCategoryJ2534);
#endif
        deviceCategories.Add(DeviceConstants.DeviceCategoryBT);
        IImmutableList<string> res = ImmutableList.CreateRange(deviceCategories);
        return ValueTask.FromResult(res);
    }

    private ValueTask<IImmutableList<SerialPortListing>> GetPortNames(CancellationToken ct)
    {
        IList<SerialPortListing> portList = new List<SerialPortListing>();
#if WINDOWS
        IEnumerable<SerialPortInfo> portNames = PortDiscovery.GetPorts(progressLogger);
        portList = [.. portNames.Where(p => !p.Name.Contains("Standard Serial over Bluetooth link")).Select(x => { return new SerialPortListing { DisplayName = x.ToString(), PortName = x.PortName }; })];#endif
        portList.Add(new SerialPortListing { DisplayName = MockPort.PortName, PortName = MockPort.PortName });
        IImmutableList<SerialPortListing> result = ImmutableList.CreateRange(portList);
        return ValueTask.FromResult(result);
    }

    private ValueTask<IImmutableList<SerialPortListing>> GetBluetoothDevices(CancellationToken ct)
    {
        List<SerialPortListing> btDevices = [];
        List<BluetoothDeviceInfo> list = SerialBluetoothDiscovery.GatherPairedDevices().ToList();
        btDevices = [.. list.Select(x => { return new SerialPortListing { DisplayName = x.DeviceName, PortName = x.DeviceAddress.ToString() }; })];
        IImmutableList<SerialPortListing> res = ImmutableList.CreateRange(btDevices);
        return ValueTask.FromResult(res);
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

    private ValueTask<bool> AreEqual(string value1, string value2)
    {
        return ValueTask.FromResult(value1 == value2);
    }

    private async ValueTask DeviceCategoryChanged<T>(T newValue, CancellationToken ct)
    {
        await UseSerialDevice.SetAsync(newValue as string == DeviceConstants.DeviceCategorySerial);
        await UseJ2534Device.SetAsync(newValue as string == DeviceConstants.DeviceCategoryJ2534);
        await UseBTDevice.SetAsync(newValue as string == DeviceConstants.DeviceCategoryBT);
        if(newValue as string == DeviceConstants.DeviceCategoryBT)
        {
#if ANDROID
            if(!await Platforms.Android.PermissionMethods.IsBluetoothGranted())
            {
                bool result = await DialogService.ShowBinaryPrompt("Request permissions",
                    "PCM Hammer requires access to nearby devices\r\n" +
                    "in order to use Bluetooth. Press \"Okay\" to be navigate to\r\n" +
                    "this permission page.", "Okay", "Cancel", PrimaryButton.Left);
        
                if (result)
                {
                    if(!await Platforms.Android.PermissionMethods.GrantBluetoothPermissions())
                    {
                        // TODO: How to handle bad user decisions?
                    }
                }
            }
#endif
        }
        _deviceRefreshSignal.Raise();
        await ConnectionSettingsChanged(newValue, ct);
    }

    private async ValueTask ConnectionSettingsChanged<T>(T newValue, CancellationToken ct)
    {
        if (deviceConnectionCancelSource != null)
        {
            deviceConnectionCancelSource.Cancel();
            deviceConnectionCancelSource.Dispose();
            deviceConnectionCancelSource = null;
        }
        string deviceCategory = await SelectedDeviceType.Value() ?? string.Empty;
        string? portName =
            deviceCategory == DeviceConstants.DeviceCategorySerial ? (await SelectedObd2Port.Value(ct) ?? new()).PortName :
            deviceCategory == DeviceConstants.DeviceCategoryJ2534 ? await SelectedJDevice.Value(ct) :
            deviceCategory == DeviceConstants.DeviceCategoryBT ? (await SelectedBluetoothDevice.Value(ct) ?? new()).PortName : "";


        CurrentSettings currentSettings = new CurrentSettings(
            deviceCategory,
            portName ?? string.Empty,
            await this.UseCanDevice.Value(),
            (await this.SelectedCanPort.Value() ?? new()).PortName ?? "");

        this.settingsService.SaveConnectionSettings(currentSettings);
        deviceConnectionCancelSource ??= new CancellationTokenSource();
        _ = Task.Run(() => this.connectionService.TryConnect(currentSettings), deviceConnectionCancelSource.Token);
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
