using System.Diagnostics;
using PcmHacking.UnoUI.Utilities;

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
    Logging = 64,
}

public class ConnectionUnavailableException : InvalidOperationException
{
    public ConnectionUnavailableException(string message) : base(message) { }
}

/// <summary>
/// This provides access to the vehicle.
/// </summary>
/// <remarks>
/// This just helps to ensure that EndActivity gets called after BeginActivity,
/// by making it possible to use the "using" pattern.
/// using (var lease = connectionService.BeginActivity(...))
/// { 
///     Vehicle vehicle = lease.Vehicle;
///     // do stuff with the vehicle....
/// } // EndActivity is automatically called here, even if an exception is thrown.
/// </remarks>
public class ConnectionLease : IDisposable
{
    private readonly ConnectionService connectionService;
    private readonly Vehicle vehicle;
    private readonly string activityName;
    private bool isDisposed = false;

    public Vehicle Vehicle
    {
        get
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(ConnectionLease));
            }
            return this.vehicle;
        }
    }

    public ConnectionLease(ConnectionService vehicleService, Vehicle vehicle, string activityName)
    {
        this.connectionService = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
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
            await this.connectionService.EndActivity(/*this?*/);
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

    Task<ConnectionLease> BeginActivity(string activity, bool canInterrupt = false);

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

    Task ResetCodes(PcmHacking.ILogger progressLogger);
}

public class ConnectionService : IConnectionService
{
    public const string PollingActivity = "Checking...";
    public const string TestingActivity = "Testing Connection...";
    private ISettingsService settingsService;
    private PcmHacking.ILogger progressLogger;
    private ILogger<ConnectionService> unoLogger;
    private Protocol protocol;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private System.Threading.Timer? timer = null;
    private ConnectionStates internalState = ConnectionStates.NotConfigured;
    private object transitionLock = new object();
    private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public IState<ConnectionStates> ConnectionState => State.Value(this, () => ConnectionStates.NotConfigured);

    public IState<string> Activity => State.Value(this, () => string.Empty);
    public IState<string> ConnectionError => State.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State.Value(this, () => string.Empty);
    public IState<string> Voltage => State.Value(this, () => string.Empty);

    public ConnectionService(
        ISettingsService settingsService,
        PcmHacking.ILogger logger, 
        ILogger<ConnectionService> unoLogger)
    {
        this.settingsService = settingsService;
        this.progressLogger = logger;
        this.unoLogger = unoLogger;
        this.protocol = new PcmHacking.Protocol();
    }

    /// <summary>
    /// This should only be used when connection settings change. Callers should generally use BeginActivity instead.
    /// </summary>
    public async Task<bool> TryConnect(CurrentSettings settings)
    {
        bool isConnected = false;
        try
        {
            // The public BeginActivity will throw if not connected, so we go
            // around it and call the private BeginActivity. That requires
            // managing the semaphore explicitly.
            this.progressLogger.AddDebugMessage("SEMAPHORE: TryConnect waiting.");
            await this.semaphore.WaitAsync();
            this.progressLogger.AddDebugMessage("SEMAPHORE: TryConnect acquired.");

            await this.BeginActivity(TestingActivity, ConnectionStates.Connecting);

            // Clear the settings shown in the UI, and allow time for the UI to update.
            await this.ResetVehicleInfo();
            await Task.Delay(100);

            if (this.vehicle != null)
            {
                this.vehicle.Dispose();
                this.vehicle = null;
            }

            // This ends up being a no-op because vehicle.Dispose() also disposes the underlying connection.
            // Not sure if that's a good thing or a bad thing, but it's probably fine.
            if (this.device != null)
            {
                this.device.Dispose();
                this.device = null;
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

            if (await this.TryPollOnce(newVehicle))
            {
                isConnected = true;
                this.device = newDevice;
                this.vehicle = newVehicle;
            }
        }
        catch (Exception exception)
        {
            // TODO: modal dialog box - this probably means that the port couldn't be opened.
            Debugger.Break();
            this.progressLogger.AddDebugMessage("Exception while connecting to vehicle.");
            this.progressLogger.AddDebugMessage(exception.ToString());
            return false;
        }
        finally
        {
            // This will release the semaphore.
            await this.EndActivityInternal(isConnected);
        }

        return isConnected;
    }

    public async Task<ConnectionLease> BeginActivity(string activity, bool canInterrupt)
    {
        ConnectionStates nextState = ConnectionStates.Active;
        switch (activity)
        {
            case TestingActivity:
                nextState = ConnectionStates.Connecting;
                break;

            case PollingActivity:
                nextState = ConnectionStates.Polling;
                break;

            default:
                nextState = canInterrupt ? ConnectionStates.Logging : ConnectionStates.Active;
                break;
        }

        this.progressLogger.AddDebugMessage($"SEMAPHORE: BeginActivity ({activity}) waiting.");
        await this.semaphore.WaitAsync();
        this.progressLogger.AddDebugMessage($"SEMAPHORE: BeginActivity ({activity}) acquired.");

        try
        {
            if (this.vehicle == null)
            {
                throw new ConnectionUnavailableException("Not connected.");
            }

            await this.BeginActivity(activity, nextState);
        }
        catch (Exception)
        {
            // If an exception is thrown (e.g. because the connection can't be
            // acquired) the 'using' pattern won't call the Dispose method.
            this.progressLogger.AddDebugMessage($"SEMAPHORE: BeginActivity ({activity}) released.");
            this.semaphore.Release();
            throw;
        }

        return new ConnectionLease(this, this.vehicle, activity);
    }

    private async Task BeginActivity(string activity, ConnectionStates desiredState)
    {
        if (string.IsNullOrEmpty(activity))
        {
            throw new System.InvalidOperationException("'activity' must not be null or empty");
        }

        string? current = await this.Activity.Value();
        string errorMessage = $"Unable to acquire connection. Beginning {activity}, current {current}";
        ConnectionStates allowed = 0;

        switch (desiredState)
        {
            // "Active" scenarios disable the back-button.
            // "Logging" scenarios allow the back-button.
            // Both of them disable polling.
            case ConnectionStates.Active:
            case ConnectionStates.Logging:
                if (!this.TryTransition(ConnectionStates.Connected, desiredState))
                {
                    throw new ConnectionUnavailableException("Not connected. " + errorMessage);
                }

                // The main reason for hiding these is that I don't want to give the user
                // a false sense of security about the voltage. It might go down while they
                // are flashing or logging, but it won't be updated in the UI.
                await this.OperatingSystemId.SetAsync(String.Empty);
                await this.Voltage.SetAsync(String.Empty);
                break;

            // Polling is triggered by a timer, and is only allowed when the
            // connection isn't being used for anything else.
            case ConnectionStates.Polling:
                allowed =
                    ConnectionStates.NotConnected |
                    ConnectionStates.Connected |
                    ConnectionStates.Connecting |
                    ConnectionStates.NotConfigured;

                if (!this.TryTransition(allowed, ConnectionStates.Polling))
                {
                    this.progressLogger.AddDebugMessage($"Skipping poll, internalState is {this.internalState}");
                    throw new ConnectionUnavailableException("Unable to poll. " + errorMessage);
                }
                break;

            // The code that tests new connection settings uses BeginActivity
            // to ensure that it doesn't interrupt other activities. This uses
            // an extra-long timeout because the previous settings might have
            // been bad, and it might take a while for connection timeouts to
            // expire.
            case ConnectionStates.Connecting:
                allowed =
                    ConnectionStates.NotConnected |
                    ConnectionStates.NotConfigured |
                    ConnectionStates.Connected |
                    ConnectionStates.Polling;
                if (!this.TryTransition(allowed, ConnectionStates.Connecting))
                {
                    throw new ConnectionUnavailableException($"This should never happen. Trying to test new settings. Current state is {this.internalState}, but: " + errorMessage);
                }
                break;

            default:
                throw new ConnectionUnavailableException($"Invalid activity state: {desiredState}");
        }

        // "Active" is used to disable the back-button.
        // The Polling activity was created to enable the back-button to stay enabled.
        //if (activity != PollingActivity)
        {
            await this.ConnectionState.SetAsync(desiredState);
        }

        await this.Activity.SetAsync(activity);

        this.StopTimer();
    }

    public async Task EndActivity()
    {
        // In this scenario we're just assuming the connection is still good.
        // It might be helpful to give the ConnectionLease object a way to indicate
        // that the connection was lost while the lease was held, but I'm not sure
        // what we'd do with that information other than just return to polling,
        // which is what happens anyway.
        //
        // The real reason for the boolean parameter is just to give the TryConnect
        // method a way to change the UI from Connected to Not Connected when the
        // connection settings aren't valid.
        //
        // In other scenarios, we're just passing 'true' and hoping for the best.
        await this.EndActivityInternal(true);
    }

    private async Task EndActivityInternal(bool isConnected)
    {
        try
        {
            // TODO: Dispose and re-create the Vehicle instance here, to ensure that it doesn't continue to get used.
            // The current implementation of Vehice.Dispose() also disposes the underlying connection, which we don't want.
            // Could probably change that without breaking the WinForms UI, but need to investigate.
            // (It's a low priority. Recreating the Vehicle is probably overkill anyway.)
            //
            // Also, for reasons unknown, the underlying serial port can't always be re-opened, especially with the ObdX driver.
            // Need to figure that out before we can re-create the Vehicle instance here.
            if (isConnected)
            {
                ConnectionStates allowed =
                    ConnectionStates.Active |
                    ConnectionStates.Logging |
                    ConnectionStates.NotConfigured |
                    ConnectionStates.NotConnected |
                    ConnectionStates.Connecting |
                    ConnectionStates.Polling;
                this.TryTransition(allowed, ConnectionStates.Connected);
                await this.ConnectionState.SetAsync(ConnectionStates.Connected);
            }
            else
            {
                this.ForceTransition(ConnectionStates.NotConnected);
                await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            }

            await this.Activity.SetAsync(String.Empty);
        }
        finally
        {
            this.progressLogger.AddDebugMessage($"SEMAPHORE: released by EndActivity.");
            this.semaphore.Release();
        }

        this.StartTimer(null);
    }

    private void StartTimer(object? state)
    {
        this.timer = new System.Threading.Timer(
            TimerCallback,
            state: state,
            dueTime: 1000,
            period: Timeout.Infinite);
    }

    private void StopTimer()
    {
        if (this.timer != null)
        {
            this.timer.Change(int.MaxValue, Timeout.Infinite);
            this.timer.Dispose();
            this.timer = null;
        }
    }

    private async void TimerCallback(object? state)
    {
        if (this.timer == null)
        {
            return;
        }

        bool disconnected = false;
        Vehicle? acquiredVehicle = null;
        try
        {
            this.progressLogger.AddDebugMessage($"ConnectionService timer callback. Internal state: {this.internalState}.");
            using (ConnectionLease lease = await this.BeginActivity(PollingActivity, true))
            {
                acquiredVehicle = lease.Vehicle;
                if (acquiredVehicle == null)
                {
                    return;
                }

                bool success = await this.TryPollOnce(acquiredVehicle);
                if (success)
                {
                    this.progressLogger.AddDebugMessage($"Poll succeeded, transitioning to Connected.");
                    this.ForceTransition(ConnectionStates.Connected);
                    if (state != null)
                    {
                        CurrentSettings? settings = state as CurrentSettings;
                        if (settings != null)
                        {
                            this.settingsService.SaveConnectionSettings(settings);
                            state = null;
                        }
                    }
                }
                else
                {
                    await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
                    disconnected = true;
                }
            }
        }
        catch (ConnectionUnavailableException)
        {
            this.progressLogger.AddDebugMessage("Poll skipped.");
        }
        catch (Exception exception)
        {
            this.unoLogger.LogError(new EventId(6, "VehicleService"), exception, "Timer callback exception.");
            disconnected = true;
        }
        finally
        {   
            if (acquiredVehicle != null)
            {
                string result = disconnected ? "but disconnected" : "and still connected";
                this.progressLogger.AddDebugMessage($"Exiting timer callback, vehicle acquired {result}");
            }
            else
            {
                this.progressLogger.AddDebugMessage("Exiting timer callback, vehicle not acquired.");
            }
        }
    }

    private async Task<bool> TryPollOnce(Vehicle vehicle)
    {
        bool success = false;

        using (var source = new CancellationTokenSource())
        {
            try
            {
                success = await TimeoutUtilities.TaskWithTimeoutAndException(
                    this.TryRequestVehicleInfo(vehicle, source.Token),
                    TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                source.Cancel();
                success = false;
                this.progressLogger.AddUserMessage("Connection test did not get a response from the vehicle.");
            }
            catch (Exception exception)
            {
                this.progressLogger.AddUserMessage("Error while testing vehicle connection.");
                this.progressLogger.AddDebugMessage(exception.ToString());
            }
        }

        if (success)
        {
            this.progressLogger.AddUserMessage("Connection test succeeded.");
            await this.ConnectionState.SetAsync(ConnectionStates.Connected);
            return true;
        }
        else
        {
            this.progressLogger.AddUserMessage("Connection test failed.");
            await this.ResetVehicleInfo();
            return false;
        }
    }

    /// <summary>
    /// This is used to poll the PCM periodically, to ensure that the connection is still good.
    /// </summary>
    private async Task<bool> TryRequestVehicleInfo(Vehicle vehicle, CancellationToken cancellationToken)
    {
        if (vehicle == null)
        {
            await this.ConnectionState.SetAsync(ConnectionStates.NotConnected);
            return false;
        }

        // Sanity check. This should never happen.
        string activityName = await this.Activity.Value(cancellationToken);
        if ((activityName != PollingActivity) && (activityName != TestingActivity))
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

    // TODO: Move this into ReadModel
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
            using (await this.BeginActivity("Reading flash", false))
            {
                try
                {
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
                }
            }
        }
        catch (Exception exception)
        {
            logger.AddUserMessage("Read failed, unable to acquire connection?");
            logger.AddUserMessage(exception.ToString());
            await Task.Delay(1000);
        }
    }

    // TODO: Move this into WriteModel
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
            using (await this.BeginActivity("Writing flash", false))
            {
                try
                {
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
                }
            }
        }
        catch (Exception exception)
        {
            logger.AddUserMessage("Write failed, unable to acquire connection?");
            logger.AddUserMessage(exception.ToString());
            await Task.Delay(1000);
        }
    }

    public async Task ResetCodes(PcmHacking.ILogger progressLogger)
    {
        using (ConnectionLease lease = await this.BeginActivity("Reset Codes", false))
        {
            Vehicle vehicle = lease.Vehicle;
            try
            {
                await vehicle.ExitKernel();
                await vehicle.ClearTroubleCodes();
            }
            catch (Exception exception)
            {
                this.progressLogger.AddUserMessage("Exception while clearing trouble codes.");
                this.progressLogger.AddDebugMessage(exception.ToString());
            }
        }
    }

    private bool TryTransition(ConnectionStates expected, ConnectionStates newState)
    {
        lock (this.transitionLock)
        {
            this.progressLogger.AddDebugMessage($"Transition requested from: {this.internalState}, to: {newState}");
            if (((this.internalState & expected) > 0) || this.internalState == newState)
            {
                this.ForceTransition(newState);
                return true;
            }

            this.progressLogger.AddDebugMessage($"Transition denied, staying in: {this.internalState}");
            return false;
        }
    }

    private void ForceTransition(ConnectionStates newState)
    {
        this.internalState = newState;
        this.progressLogger.AddDebugMessage($"Transitioned to: {newState}");
    }
}
