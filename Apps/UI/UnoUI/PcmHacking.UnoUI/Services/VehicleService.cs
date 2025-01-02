using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using PcmHacking;

namespace PcmHacking.UnoUI.Services;

public enum ConnectionState
{
    NotConfigured,
    NotConnected,
    Connected,
}

public enum VehicleServiceState
{
    Startup,
    Connecting,
    Polling,
    InUse,
}

public partial record VehicleInfo(ConnectionState connectionState, string errorMessage, string operatingSystemId, string voltage);

public interface IVehicleService
{
    Task Reset();
    Task StopPolling();
    void StartPolling();
}

public class VehicleService : IVehicleService
{
    private PcmHacking.ILogger logger;
    private ILogger<VehicleService> unoLogger;
    private Protocol protocol;
    private ConnectionState connectionState = ConnectionState.NotConfigured;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private IMessenger messenger;
    private VehicleServiceState state = VehicleServiceState.Startup;

    // This may or may not be the best way to do this. For alternatives, see this example:
    // https://github.com/MartinZikmund/coffee-breaks/blob/main/UnoTimers/UnoTimers.Shared/MainPage.xaml.cs
    private System.Threading.Timer? threadingTimer;

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

    public static readonly VehicleInfo StartupVehicleInfo = new VehicleInfo(ConnectionState.NotConfigured, "Starting up...", String.Empty, String.Empty);

    public async Task Reset()
    {
        if (this.vehicle != null)
        {
            this.vehicle.Dispose();
            this.vehicle = null;
        }

        this.state = VehicleServiceState.Connecting;
        VehicleInfo vehicleInfo = await this.GetVehicleInfoAsync(CancellationToken.None);
        this.messenger.Send(vehicleInfo);

        this.threadingTimer ??= new System.Threading.Timer(
            ThreadingTimerCallback,
            state: null,
            dueTime: 100,
            period: 100);
    }

    private async void ThreadingTimerCallback(object? state)
    {
        if (this.threadingTimer == null)
        {
            this.unoLogger.LogError("ThreadingTimerCallback called with null timer.");
            return;
        }

        VehicleInfo vehicleInfo = await this.GetVehicleInfoAsync(CancellationToken.None);
        this.messenger.Send(vehicleInfo);
        if (this.state == VehicleServiceState.Connecting && vehicleInfo.connectionState == ConnectionState.Connected)
        {
            this.state = VehicleServiceState.Polling;
            await this.threadingTimer.DisposeAsync();
            this.threadingTimer = new System.Threading.Timer(
                ThreadingTimerCallback,
                state: null,
                dueTime: 500,
                period: 500);
        }
        else if (this.state == VehicleServiceState.Polling && vehicleInfo.connectionState == ConnectionState.NotConnected)
        {
            await this.threadingTimer.DisposeAsync();
            this.state = VehicleServiceState.Connecting;
            this.threadingTimer = new System.Threading.Timer(
                ThreadingTimerCallback,
                state: null,
                dueTime: 100,
                period: 100);
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

    /// <summary>
    /// The caller is expecte to invoke this method repeatedly. When the 
    /// connection state is ConnectionState.Connected, the polling should stop,
    /// and flashing or logging can begin.
    /// </summary>
    private async Task<VehicleInfo> GetVehicleInfoAsync(CancellationToken cancellationToken)
    {
        if (this.device == null)
        {
            this.device = DeviceFactory.CreateDeviceFromConfigurationSettings(this.logger);
        }

        if (this.device == null)
        {
            this.connectionState = ConnectionState.NotConfigured;
            return new VehicleInfo(this.connectionState, "Please select a device.", String.Empty, string.Empty);
        }

        if (this.connectionState == ConnectionState.NotConfigured)
        {
            this.connectionState = ConnectionState.NotConnected;
            ToolPresentNotifier notifier = new ToolPresentNotifier(this.device, this.protocol, this.logger);
            this.vehicle = new Vehicle(device, this.protocol, this.logger, notifier);
            return new VehicleInfo(connectionState, "Connecting to vehicle...", String.Empty, string.Empty);
        }

        VehicleInfo vehicleInfo = await this.GetVehicleInfo(cancellationToken);
        this.connectionState = vehicleInfo.connectionState;
        return vehicleInfo;
    }

    private async Task<VehicleInfo> GetVehicleInfo(CancellationToken cancellationToken)
    {
        if (this.vehicle != null)
        {
            uint osid = 0;
            Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(cancellationToken);
            if (osidResponse.Status == ResponseStatus.Success)
            {
                osid = osidResponse.Value;
            }
            else
            {
                return new VehicleInfo(ConnectionState.NotConnected, osidResponse.Status.ToString(), String.Empty, String.Empty);
            }

            string voltage = String.Empty;
            Response<string> voltageResponse = await this.vehicle.QueryVoltage();
            if (voltageResponse.Status == ResponseStatus.Success)
            {
                voltage = voltageResponse.Value;
            }
            else
            {
                return new VehicleInfo(ConnectionState.NotConnected, voltageResponse.Status.ToString(), osid.ToString(), String.Empty);
            }
            return new VehicleInfo(ConnectionState.Connected, String.Empty, osid.ToString(), voltage);
        }
        return new VehicleInfo(ConnectionState.NotConnected, "Vehicle is not connected", String.Empty, string.Empty);
    }
}
