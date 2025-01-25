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

public class ConnectionStateChangedMessage { }

public interface IVehicleService
{
    IState<ConnectionStates> ConnectionState { get; }
    IState<string> ConnectionError { get; }

    IState<string> OperatingSystemId { get; }

    IState<string> Voltage { get; }

    Task<bool> TryConnect(CurrentSettings settings);

    void SchedulePoll();
}

public class VehicleService : IVehicleService
{
    private PcmHacking.ILogger progressLogger;
    private ILogger<VehicleService> unoLogger;
    private Protocol protocol;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private IMessenger messenger;

    // Simplified name
    private System.Threading.Timer? threadingTimer;

    public IState<ConnectionStates> ConnectionState => State.Value(this, () => ConnectionStates.NotConfigured);
    public IState<string> ConnectionError => State.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State.Value(this, () => string.Empty);
    public IState<string> Voltage => State.Value(this, () => string.Empty);

    public VehicleService(
        PcmHacking.ILogger logger, 
        IMessenger messenger, 
        ILogger<VehicleService> unoLogger)
    {
        this.progressLogger = logger;
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
            this.progressLogger, 
            settings.DeviceCategory, 
            settings.Obd2SerialPortName, 
            settings.Obd2SerialDeviceName,
            settings.J2534DeviceName);

        if (this.device == null)
        {
            return false;
        }

        ToolPresentNotifier notifier = new ToolPresentNotifier(this.device, this.protocol, this.progressLogger);
        this.vehicle = new Vehicle(device, this.protocol, this.progressLogger, notifier);
        await this.ConnectionState.SetAsync(ConnectionStates.Connecting);
        
        if (await this.TryRequestVehicleInfo(CancellationToken.None))
        {
            await this.ConnectionState.SetAsync(ConnectionStates.Connected);
            this.SchedulePoll();
            return true;
        }
        else
        {
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            return false;
        }
    }

    public void SchedulePoll()
    {
        this.threadingTimer ??= new System.Threading.Timer(
            ThreadingTimerCallback,
            state: null,
            dueTime: 2000,
            period: Timeout.Infinite);
    }

    private async void ThreadingTimerCallback(object? state)
    {
        if (this.threadingTimer == null)
        {
            this.unoLogger.LogError("ThreadingTimerCallback called with null timer.");
            return;
        }

        if (await this.TryRequestVehicleInfo(CancellationToken.None))
        {
            this.SchedulePoll();
        }
    }

    /// <summary>
    /// The caller is expected to invoke this method repeatedly. When the 
    /// connection state is ConnectionState.Connected, the polling should stop,
    /// and flashing or logging can begin.
    /// </summary>
    private async Task<bool> TryRequestVehicleInfo(CancellationToken cancellationToken)
    {
        if ((this.device == null) || (this.vehicle == null))
        {
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            return false;
        }

        try
        {
            Response<uint> osidResponse = await this.vehicle.QueryOperatingSystemId(cancellationToken);
            if (osidResponse.Status == ResponseStatus.Success)
            {
                await this.OperatingSystemId.SetAsync(osidResponse.Value.ToString());
            }
            else
            {
                await this.ResetVehicleInfo();
                return false;
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
                return false;
            }
        }
        catch (Exception exception)
        {
            this.progressLogger.AddUserMessage("Internal error while polling: " + exception.ToString());
            return false;
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
