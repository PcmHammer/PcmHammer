using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using PcmHacking;

namespace PcmHacking.UnoUI.Services;

public enum ConnectionStates
{
    NotConfigured,
    NotConnected,
    Connecting,
    Connected,
    InUse,
}

public interface IVehicleService
{
    IState<ConnectionStates> ConnectionState { get; }
    Task<bool> TryConnect(CurrentSettings settings);
    Task StopPolling();
    void StartPolling();
}

public class VehicleService : IVehicleService
{
    private PcmHacking.ILogger logger;
    private ILogger<VehicleService> unoLogger;
    private Protocol protocol;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private IMessenger messenger;

    // This may or may not be the best way to do this. For alternatives, see this example:
    // https://github.com/MartinZikmund/coffee-breaks/blob/main/UnoTimers/UnoTimers.Shared/MainPage.xaml.cs
    private System.Threading.Timer? threadingTimer;

    public IState<ConnectionStates> ConnectionState => State<ConnectionStates>.Value(this, () => ConnectionStates.NotConfigured);
    public IState<string> ConnectionError => State<string>.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State<string>.Value(this, () => string.Empty);
    public IState<string> Voltage => State<string>.Value(this, () => string.Empty);

    public VehicleService(
        PcmHacking.ILogger logger, 
        IMessenger messenger, 
        ILogger<VehicleService> unoLogger)
    {
        this.logger = logger;
        this.unoLogger = unoLogger;
        this.protocol = new PcmHacking.Protocol();
        this.messenger = messenger;
    }

    public async Task<bool> TryConnect(CurrentSettings settings)
    {
        if (await this.ConnectionState.Value() == ConnectionStates.InUse)
        {
            return false;
        }

        await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);

        if (this.vehicle != null)
        {
            this.vehicle.Dispose();
            this.vehicle = null;
        }

        this.device = DeviceFactory.CreateDevice(
            this.logger, 
            settings.DeviceCategory, 
            settings.Obd2SerialPortName, 
            settings.Obd2SerialDeviceType,
            "J2534 Not Yet Implemented");

        if (this.device == null)
        {
            return false;
        }

        this.vehicle = new Vehicle(this.device, this.protocol, this.logger, new ToolPresentNotifier(this.device, this.protocol, this.logger));

        await this.ConnectionState.SetAsync(ConnectionStates.Connecting);

        if (await this.TryRequestVehicleInfo(CancellationToken.None))
        {
            await this.ConnectionState.SetAsync(ConnectionStates.Connected);
            this.StartPolling();
            return true;
        }
        else
        {
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            await this.StopPolling();
            return false;
        }
    }
    
    public async Task StopPolling()
    {
        if (this.threadingTimer == null)
        {
            this.unoLogger.LogError("StopPolling called with null timer.");
            return; 
        }

        await this.threadingTimer.DisposeAsync();
    }

    public void StartPolling()
    {
        this.threadingTimer ??= new System.Threading.Timer(
            ThreadingTimerCallback,
            state: null,
            dueTime: 500,
            period: 500);
    }

    private async void ThreadingTimerCallback(object? state)
    {
        if (this.threadingTimer == null)
        {
            this.unoLogger.LogError("ThreadingTimerCallback called with null timer.");
            return;
        }

        if (!await this.TryRequestVehicleInfo(CancellationToken.None))
        {
            await this.StopPolling();
        }
    }

    /// <summary>
    /// The caller is expected to invoke this method repeatedly. When the 
    /// connection state is ConnectionState.Connected, the polling should stop,
    /// and flashing or logging can begin.
    /// </summary>
    private async Task<bool> TryRequestVehicleInfo(CancellationToken cancellationToken)
    {
        if (this.device == null)
        {
            return false;
        }

        await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
        ToolPresentNotifier notifier = new ToolPresentNotifier(this.device, this.protocol, this.logger);
        this.vehicle = new Vehicle(device, this.protocol, this.logger, notifier);

        if (this.vehicle == null)
        {
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            return false;
        }

        Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(cancellationToken);
        if (osidResponse.Status == ResponseStatus.Success)
        {
            await this.OperatingSystemId.SetAsync(osidResponse.Value.ToString());
        }
        else
        {
            await this.ResetVehicleInfo();
        }

        string voltage = String.Empty;
        Response<string> voltageResponse = await this.vehicle.QueryVoltage();
        if (voltageResponse.Status == ResponseStatus.Success)
        {
            await this.Voltage.SetAsync(voltageResponse.Value ?? String.Empty);
        }
        else
        {
            await this.ResetVehicleInfo();
        }

        return true;
    }

    private async Task ResetVehicleInfo()
    {
        await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
        await this.OperatingSystemId.SetAsync(String.Empty);
        await this.Voltage.SetAsync(String.Empty);
    }
}
