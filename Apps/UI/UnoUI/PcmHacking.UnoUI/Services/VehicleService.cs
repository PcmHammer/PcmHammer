using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
    Active,
}

public class ConnectionStateChangedMessage { }

public interface IVehicleService
{
    IState<ConnectionStates> ConnectionState { get; }

    IState<string> Activity { get; }

    IState<string> ConnectionError { get; }

    IState<string> OperatingSystemId { get; }

    IState<string> Voltage { get; }

    Task<bool> TryConnect(CurrentSettings settings);

    Task<Vehicle?> TryBeginActivity(string activity);

    Task EndActivity();
}

public class VehicleService : IVehicleService
{
    private const string pollingActivity = "Connected";
    private PcmHacking.ILogger progressLogger;
    private ILogger<VehicleService> unoLogger;
    private Protocol protocol;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private System.Threading.Timer? timer = null;

    public IState<ConnectionStates> ConnectionState => State.Value(this, () => ConnectionStates.NotConfigured);

    public IState<string> Activity => State.Value(this, () => string.Empty);
    public IState<string> ConnectionError => State.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State.Value(this, () => string.Empty);
    public IState<string> Voltage => State.Value(this, () => string.Empty);

    public VehicleService(
        PcmHacking.ILogger logger, 
        ILogger<VehicleService> unoLogger)
    {
        this.progressLogger = logger;
        this.unoLogger = unoLogger;
        this.protocol = new PcmHacking.Protocol();
    }

    public async Task<bool> TryConnect(CurrentSettings settings)
    {
        if (await this.ConnectionState.Value() == ConnectionStates.Active)
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
        
        if (await this.TryPollOnce())
        {
            this.unoLogger.LogInformation(new EventId(1, "VehicleService"), "First poll succeeded.");
            this.progressLogger.AddDebugMessage("First poll succeeded.");
            await this.ConnectionState.SetAsync(ConnectionStates.Connected);
            return true;
        }
        else
        {
            this.unoLogger.LogInformation(new EventId(2, "VehicleService"), "First poll failed.");
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            return false;
        }
    }

    public async Task<Vehicle?> TryBeginActivity(string activity)
    {
        if (this.timer != null)
        {
            this.timer.Dispose();
            this.timer = null;
        }

        if (await this.ConnectionState.Value() == ConnectionStates.Active)
        {
            this.unoLogger.LogError(new EventId(7, "VehicleService"), "Attempting to use an active connection.");
            return null;
        }

        await this.Activity.SetAsync(activity);
        await this.ConnectionState.SetAsync(ConnectionStates.Active);
        return this.vehicle!;
    }

    public async Task EndActivity()
    {
        // TODO: Dispose and re-create the Vehicle instance here, to ensure that it doesn't continue to get used.
        // The current implementation of Vehice.Dispose() also disposes the underlying connection, which we don't want.
        // Could probably change that without breaking the WinForms UI, but need to investigate.
        await this.ConnectionState.SetAsync(ConnectionStates.Connected);
        await this.Activity.SetAsync(String.Empty);

        this.timer = new System.Threading.Timer(
            TimerCallback,
            state: null,
            dueTime: 1000,
            period: Timeout.Infinite);
    }

    private async void TimerCallback(object? state)
    {
        // TODO: Why does this get logged despite the log level being set to Error?
        // this.unoLogger.LogInformation(new EventId(3, "VehicleService"), "Timer callback invoked.");
        try
        {
            await this.TryPollOnce();
        }
        catch (Exception exception)
        {
            this.unoLogger.LogError(new EventId(6, "VehicleService"), exception, "Timer callback exception.");
        }
    }

    private async Task<bool> TryPollOnce()
    { 
        Vehicle? acquired = await this.TryBeginActivity(pollingActivity);
        if (acquired == null)
        {
            this.unoLogger.LogError(new EventId(4, "VehicleService"), "Unable to start poll activity.");
            return false;
        }

        // TODO: Is it going to be a problem if we keep trying to poll the vehicle even after the connection is lost?
        // If so, we should stop polling in that case. Currently we will just keep trying.
        bool result = await this.TryRequestVehicleInfo(CancellationToken.None);

        await this.EndActivity();

        return result;
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

        if (await this.Activity.Value() != pollingActivity)
        {
            return false;
        }

        try
        {
            await this.OperatingSystemId.SetAsync(String.Empty);
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

            await this.Voltage.SetAsync(String.Empty);
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
            this.unoLogger.LogError(new EventId(5, "VehicleService"), exception, "Communications exception.");
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
