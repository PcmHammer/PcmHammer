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
using Uno.Extensions.Navigation;

namespace PcmHacking.UnoUI.Services;

[Flags]
public enum ConnectionStates
{
    Invalid = 0,
    NotConfigured = 1,
    NotConnected = 2,
    Connecting = 4,
    Connected = 8,
    Polling = 16,
    Active = 32,
}

public class VehicleActivity : IDisposable
{
    private readonly ConnectionService vehicleService;
    private readonly Vehicle vehicle;
    private readonly string activityName;
    private bool isDisposed = false;

    public VehicleActivity(ConnectionService vehicleService, Vehicle vehicle, string activityName)
    {
        this.vehicleService = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
        this.vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
        this.activityName = activityName ?? throw new ArgumentNullException(nameof(activityName));
    }

    public void Dispose()
    {        
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected async virtual void Dispose(bool isDisposing)
    {
        if (this.isDisposed)
        {
            return;
        }

        if (isDisposing)
        {
            await this.vehicleService.EndActivity(/*this?*/);
        }

        this.isDisposed = true;
    }
}

public interface IConnectionService
{
    IState<ConnectionStates> ConnectionState { get; }

    IState<string> Activity { get; }

    IState<string> ConnectionError { get; }

    IState<string> OperatingSystemId { get; }

    IState<string> Voltage { get; }

    Task<bool> TryConnect(CurrentSettings settings);

    Task<Vehicle> BeginActivity(string activity);

    Task EndActivity();

    Task ReadFlash(
        PcmHacking.ILogger logger,
        Func<Action, Task> invoke,
        Func<Task<string>> promptForFilePath,
        Func<Task<UInt32>> promptForOperatingSystemId,
        Func<string, string, Task> alert,
        Func<string, string, Task<bool>> promptForYesNo,
        string path,
        CancellationToken cancellationToken);

    Task WriteFlash(
        PcmHacking.ILogger logger,
        Func<string, string, Task> alert,
        Func<string, string, Task<bool>> promptForYesNo,
        WriteType writeType,
        string path,
        CancellationToken cancellationToken);

    Task<bool> TryResetCodes(PcmHacking.ILogger progressLogger);
}

public class ConnectionService : IConnectionService
{
    public const string PollingActivity = "Checking...";
    private PcmHacking.ILogger progressLogger;
    private ILogger<ConnectionService> unoLogger;
    private Protocol protocol;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private System.Threading.Timer? timer = null;
    private ConnectionStates internalState = ConnectionStates.NotConfigured;
    private object transitionLock = new object();

    public IState<ConnectionStates> ConnectionState => State.Value(this, () => ConnectionStates.NotConfigured);

    public IState<string> Activity => State.Value(this, () => string.Empty);
    public IState<string> ConnectionError => State.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State.Value(this, () => string.Empty);
    public IState<string> Voltage => State.Value(this, () => string.Empty);

    public ConnectionService(
        PcmHacking.ILogger logger, 
        ILogger<ConnectionService> unoLogger)
    {
        this.progressLogger = logger;
        this.unoLogger = unoLogger;
        this.protocol = new PcmHacking.Protocol();
    }

    /// <summary>
    /// This should only be used when connection settings change. Callers should generally use BeginActivity instead.
    /// </summary>
    public async Task<bool> TryConnect(CurrentSettings settings)
    {
        if (this.internalState != ConnectionStates.NotConfigured &&
            await this.BeginActivity("Testing Connection", ConnectionStates.NotConfigured) == null)
        {
            return false;
        }

        try
        {
            await this.ResetVehicleInfo();
            await Task.Delay(100);

            if (this.vehicle != null)
            {
                this.vehicle.Dispose();
                this.vehicle = null;
            }

            Device newDevice = DeviceFactory.CreateDevice(
                this.progressLogger,
                settings.DeviceCategory,
                settings.Obd2SerialPortName,
                settings.Obd2SerialDeviceName,
                settings.J2534DeviceName);

            if (newDevice == null)
            {
                return false;
            }

            await newDevice.Initialize();

            ToolPresentNotifier notifier = new ToolPresentNotifier(newDevice, this.protocol, this.progressLogger);
            Vehicle newVehicle = new Vehicle(newDevice, this.protocol, this.progressLogger, notifier);
            await this.ConnectionState.SetAsync(ConnectionStates.Connecting);

            await this.Activity.SetAsync(PollingActivity);
            if (await this.TryPollOnce(newVehicle))
            {
                this.device = newDevice;
                this.vehicle = newVehicle;
                this.unoLogger.LogInformation(new EventId(1, "VehicleService"), "First poll succeeded.");
                this.progressLogger.AddDebugMessage("First poll succeeded.");
                this.ForceTransition(ConnectionStates.Connected);
                await this.ConnectionState.SetAsync(ConnectionStates.Connected);

                // Pretend we just finished a poll, so the UI will update and the poll timer will start.
                await this.EndActivity();
                return true;
            }
            else
            {
                this.unoLogger.LogInformation(new EventId(2, "VehicleService"), "First poll failed.");
                await this.Activity.SetAsync("Not Configured");
                this.ForceTransition(ConnectionStates.NotConfigured);
                await this.ConnectionState.SetAsync(ConnectionStates.NotConfigured);
                await this.ResetVehicleInfo();
                return false;
            }
        }
        catch (Exception exception)
        {
            this.progressLogger.AddDebugMessage("Exception while connecting to vehicle.");
            this.progressLogger.AddDebugMessage(exception.ToString());
            return false;
        }
    }


    public async Task<Vehicle> BeginActivity(string activity)
    {
        return await this.BeginActivity(activity, ConnectionStates.Active);
    }

    private async Task<Vehicle> BeginActivity(string activity, ConnectionStates activityState)
    {
        if (string.IsNullOrEmpty(activity))
        {
            throw new System.InvalidOperationException("'activity' must not be null or empty");
        }

        string? current = await this.Activity.Value();
        string errorMessage = $"Unable to acquire connection. Beginning ${activity}, current ${current}";

        if (activityState == ConnectionStates.Active)
        {
            if (!this.TryTransition(ConnectionStates.Connected, ConnectionStates.Active, 1000))
            {
                throw new InvalidOperationException("Not connected. " + errorMessage);
            }
        }
        else if (activityState == ConnectionStates.Polling)
        {
            // Note that "not configured" is NOT an allowed state in this scenario.
            // If the user tries an unsuccessful configuration, we go into that state to disable polling.
            // If the connection is lost unexpectedly, polling should re-establish it.
            if (!this.TryTransition(ConnectionStates.Connected | ConnectionStates.NotConnected, ConnectionStates.Polling, 1000))
            {
                throw new InvalidOperationException("Unabe to poll. " + errorMessage);
            }
        }
        else if (activityState == ConnectionStates.NotConfigured)
        {
            ConnectionStates allowed =
                ConnectionStates.NotConnected |
                ConnectionStates.NotConfigured |
                ConnectionStates.Connected;
            if (!this.TryTransition(allowed, ConnectionStates.NotConfigured, 1000))
            {
                throw new InvalidOperationException("Connection lost. " + errorMessage);
            }
        }
        else
        {
            throw new InvalidOperationException($"Invalid activity state: {activityState}");
        }

        // "Active" is used to disable the back-button, so polling doesn't really count.
        if (activity != PollingActivity)
        {
            await this.ConnectionState.SetAsync(ConnectionStates.Active);
        }
                
        await this.Activity.SetAsync(activity);

        if (this.timer != null)
        {
            this.timer.Change(int.MaxValue, Timeout.Infinite);
            this.timer.Dispose();
            this.timer = null;
        }

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
        Vehicle acquiredVehicle = await this.BeginActivity(PollingActivity, ConnectionStates.Polling);
        if (acquiredVehicle == null)
        {
            return;
        }

        bool success = false;
        try
        {
            success = await this.TryPollOnce(acquiredVehicle);
        }
        catch (Exception exception)
        {
            this.unoLogger.LogError(new EventId(6, "VehicleService"), exception, "Timer callback exception.");
        }
        finally
        {
            if (success)
            {
                this.ForceTransition(ConnectionStates.Connected);
                await this.EndActivity();
            }
            else
            {
                await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            }
        }
    }

    private async Task<bool> TryPollOnce(Vehicle vehicle)
    {
        // TODO: Is it going to be a problem if we keep trying to poll the vehicle even after the connection is lost?
        // If so, we should stop polling in that case. Currently we will just keep trying.
        if (await this.TryRequestVehicleInfo(vehicle, CancellationToken.None))
        {
            await this.ConnectionState.SetAsync(ConnectionStates.Connected);
            return true;
        }
        else
        {
            await this.ResetVehicleInfo();
            return false;
        }
    }

    /// <summary>
    /// The caller is expected to invoke this method repeatedly. When the 
    /// connection state is ConnectionState.Connected, the polling should stop,
    /// and flashing or logging can begin.
    /// </summary>
    public async Task<bool> TryRequestVehicleInfo(Vehicle vehicle, CancellationToken cancellationToken)
    {
        if (vehicle == null)
        {
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            Debugger.Break();
            return false;
        }

        if (await this.Activity.Value() != PollingActivity)
        {
            Debugger.Break();
            return false;
        }

        try
        {
            await this.OperatingSystemId.SetAsync(String.Empty);
            Response<uint> osidResponse = await vehicle.QueryOperatingSystemId(cancellationToken);
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
            Response<string> voltageResponse = await vehicle.QueryVoltage();
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

    public async Task ReadFlash(
        PcmHacking.ILogger logger,
        Func<Action, Task> invoke,
        Func<Task<string>> promptForFilePath,
        Func<Task<UInt32>> promptForOperatingSystemId,
        Func<string, string, Task> alert,
        Func<string, string, Task<bool>> promptForYesNo,
        string path,
        CancellationToken cancellationToken)
    {
        if (this.vehicle is null)
        {
            throw new InvalidOperationException("Vehicle not connected.");
        }

        try
        {
            await this.BeginActivity("Reading flash");
            ReadManager readManager = new(
                logger,
                this.vehicle,
                invoke,
                promptForFilePath,
                promptForOperatingSystemId,
                alert,
                promptForYesNo,
                cancellationToken);
            await readManager.Read(path);
        }
        catch (Exception exception)
        {
            logger.AddUserMessage("Read failed.");
            logger.AddUserMessage(exception.ToString());
            await Task.Delay(1000);
        }
        finally
        {
            await this.vehicle.ExitKernel();
            await this.vehicle.ClearTroubleCodes();
            await this.EndActivity();
        }
    }

    public async Task WriteFlash(
        PcmHacking.ILogger logger,
        Func<string, string, Task> alert,
        Func<string, string, Task<bool>> promptForYesNo,
        WriteType writeType,
        string path,
        CancellationToken cancellationToken)
    {
        if (this.vehicle is null)
        {
            throw new InvalidOperationException("Vehicle not connected.");
        }

        try
        {
            await this.BeginActivity("Writing flash");
            WriteManager writeManager = new(
                logger,
                this.vehicle,
                writeType,
                alert,
                promptForYesNo,
                cancellationToken);
            await writeManager.Write(path);
        }
        catch (Exception exception)
        {
            logger.AddUserMessage("Write failed.");
            logger.AddUserMessage(exception.ToString());
            await Task.Delay(1000);
        }
        finally
        {
            await this.vehicle.ExitKernel();
            await this.vehicle.ClearTroubleCodes();
            await this.EndActivity();
        }
    }

    public async Task<bool> TryResetCodes(PcmHacking.ILogger progressLogger)
    {
        try
        {
            Vehicle vehicle = await this.BeginActivity("Clearing Codes");
            await vehicle.ExitKernel();
            await vehicle.ClearTroubleCodes();
            return true;
        }
        catch (Exception exception)
        {
            this.progressLogger.AddUserMessage("Exception while clearing trouble codes.");
            this.progressLogger.AddDebugMessage(exception.ToString());
            return false;
        }
        finally
        {
            await this.EndActivity();
        }
    }

    private bool TryTransition(ConnectionStates expected, ConnectionStates newState)
    {
        lock (this.transitionLock)
        {
            this.progressLogger.AddDebugMessage($"Transition from: {this.internalState}, to: {newState}");
            if (((this.internalState & expected) > 0) || this.internalState == newState)
            {
                this.ForceTransition(newState);
                this.progressLogger.AddDebugMessage($"Transitioned to: {newState}");
                return true;
            }

            return false;
        }
    }

    private bool TryTransition(ConnectionStates expected, ConnectionStates newState, int timeout)
    {
        int start = Environment.TickCount;
        while (Environment.TickCount - start < timeout)
        {
            if (this.TryTransition(expected, newState))
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return false;
    }

    private void ForceTransition(ConnectionStates newState)
    {
        this.internalState = newState;
    }
}
