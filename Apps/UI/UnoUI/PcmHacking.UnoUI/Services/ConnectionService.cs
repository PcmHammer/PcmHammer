// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking;
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
    private readonly string activityName;
    private Vehicle vehicle;
    private bool isDisposed = false;
    private bool connectionLost = false;

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

    public bool ConnectionLost
    {
        get { return this.connectionLost; }
        set { this.connectionLost = value; }
    }

    public ConnectionLease(ConnectionService vehicleService, Vehicle vehicle, string activityName)
    {
        this.connectionService = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
        this.vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
        this.activityName = activityName ?? throw new ArgumentNullException(nameof(activityName));
    }

    public async Task Reconnect()
    {
        this.vehicle = await this.connectionService.Reconnect();
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
            await this.connectionService.EndActivity(!this.connectionLost);
        }

        this.isDisposed = true;
    }
}

public interface IConnectionService
{
    IState<string> DeviceName { get; }
    IState<string> DeviceState { get; }
    IState<ConnectionStates> ConnectionState { get; }
    IState<string> Activity { get; }
    IState<string> ConnectionError { get; }
    IState<string> OperatingSystemId { get; }
    IState<string> Voltage { get; }
    int ResetTimeRemaining { get; }

    Task<bool> TryConnect(CurrentSettings settings);
    Task<ConnectionLease> BeginActivity(string activity, bool canInterrupt = false);
}

public class ConnectionService : IConnectionService
{
    public const string PollingActivity = "Checking...";
    public const string TestingActivity = "Testing Connection...";

    // Fast retry is used when the user is waiting to reconnect.
    // Making this faster caused new problems and didn't reconnect faster.
    private const int FastRetryPeriod = 1000;

    // Slow retry is used when app is idle.
    private const int SlowRetryPeriod = 1000;

    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter logger;
    private readonly ILogBuffer logBuffer;

    private Protocol protocol;
    private Device? device = null;
    private Vehicle? vehicle = null;
    private System.Threading.Timer? timer = null;
    private ConnectionStates internalState = ConnectionStates.NotConfigured;
    private SemaphoreSlim stateChangeSemaphore = new SemaphoreSlim(1, 1);
    private CurrentSettings? newSettings;
    private CurrentSettings? lastSettings;
    private int retryPeriod = SlowRetryPeriod;
    private DateTime _leftActiveState = DateTime.MinValue;
    private const string _recoveryString = "** RECOVERY **";
    private const string _kernelString = "** KERNEL **";

    public IState<string> DeviceName => State.Value(this, () => string.Empty);
    public IState<string> DeviceState => State.Value(this, () => string.Empty);
    public IState<ConnectionStates> ConnectionState => State.Value(this, () => ConnectionStates.NotConfigured);
    public IState<string> Activity => State.Value(this, () => string.Empty);
    public IState<string> ConnectionError => State.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State.Value(this, () => string.Empty);
    public IState<string> Voltage => State.Value(this, () => string.Empty);

    public ConnectionService(
        ISettingsService settingsService,
        LoggerAdapter logger,
        ILogBuffer logBuffer)
    {
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
        this.protocol = new PcmHacking.Protocol();
    }

    public int ResetTimeRemaining
    {
        get
        {
            if (_leftActiveState != DateTime.MinValue && DateTime.Now < _leftActiveState + TimeSpan.FromSeconds(10))
            {
                DateTime exitTime = _leftActiveState + TimeSpan.FromSeconds(10);
                return (exitTime - DateTime.Now).Seconds;
            }
            _leftActiveState = DateTime.MinValue;
            return -1;
        }
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
            // around it and call the private BeginActivity. That requires us
            // to acquire the semaphore explicitly first.
            await this.stateChangeSemaphore.WaitAsync();

            await this.BeginActivityInternal(TestingActivity, ConnectionStates.Connecting);

            // Clear the settings shown in the UI, and allow time for the UI to update.
            await this.ResetVehicleInfo();
            await Task.Delay(100);

            (Device? newDevice, Vehicle? newVehicle) = await TryReconnect(settings);

            if (newDevice == null || newVehicle == null)
            {
                await DeviceState.SetAsync("Faulted");
                return false;
            }
            await DeviceState.SetAsync("Connected");

            if (await this.TryPollOnce(newVehicle))
            {
                this.logger.AddUserMessage("PCM Hammer");
#if !ANDROID
                this.logger.AddUserMessage(AppInfo.GetVersionOrBuildLine(Generated.BuildTime));
                this.logger.AddUserMessage(AppInfo.GetRunningAtMessage());
#else
                this.logger.AddUserMessage("Running at: " + DateTime.Now.ToString("dddd, MMMM dd yyyy, HH:mm:ss"));
#endif
                this.logger.AddUserMessage("Copyright (C) 2018-2026 PcmHacking.net - GPL v3");
                this.logger.AddUserMessage("Connection test succeeded.");
                this.newSettings = settings;
                this.lastSettings = settings;
                this.settingsService.SaveConnectionSettings(settings);
                isConnected = true;
                this.device = newDevice;
                this.vehicle = newVehicle;
            }
            else
            {
                this.logger.AddUserMessage("Connection test failed.");
                this.newSettings = settings;
                if(this.lastSettings == null)
                {
                    this.lastSettings = newSettings; // This avoids inactivity if device/PCM fails first try, unless this was intended.
                }
                newVehicle.Dispose();
                newVehicle = null;
                newDevice.Dispose();
                newDevice = null;
            }

        }
        catch (Exception exception)
        {
            this.newSettings = settings;
            await DeviceState.SetAsync("Faulted");
            this.logger.AddDebugMessage("Exception while connecting to vehicle.");
            this.logger.AddDebugMessage(exception.ToString());
            return false;
        }
        finally
        {
            // This will release the semaphore.
            await this.EndActivity(isConnected);
        }

        return isConnected;
    }

    /// <summary>
    /// Reconnect after a connection loss.
    /// </summary>
    /// <remarks>
    /// This should only be invoked from ConnectionLease.Reconnect(), because
    /// it assumes that the caller holds the semaphore.
    /// </remarks>
    public async Task<Vehicle?> Reconnect()
    {
        (Device? newDevice, Vehicle? newVehicle) = await TryReconnect(this.lastSettings);
        this.device = newDevice;
        this.vehicle = newVehicle;
        return this.vehicle;
    }

    private async Task<(Device? newDevice, Vehicle? newVehicle)> TryReconnect(CurrentSettings settings)
    {
        if (App.ApplicationShutdownSource.IsCancellationRequested && this.vehicle != null)
        {
            this.vehicle.ShutdownSignalSource.Cancel();
            this.vehicle?.Dispose();
            return (null, null);
        }
        if (this.vehicle != null)
        {
            this.vehicle.Dispose();
            this.vehicle = null;
        }
        if (this.device != null)
        {
            try
            {
                if (!await this.device.CheckDeviceConnection())
                {
                    this.device.Dispose();
                    this.device = null;
                }
            }
            catch
            {
                this.device.Dispose();
                this.device = null;
            }
        }

        Device? newDevice = null;
        string portDesc = settings.DeviceCategory == DeviceConstants.DeviceCategorySerial ? settings.DeviceNameOrPort : settings.DeviceCategory;
        if (portDesc == DeviceConstants.DeviceCategoryBT) // Only just to shorten in UI.
        {
            portDesc = "BT";
        }
        if (this.device == null || settings != this.newSettings)
        {
            if (string.IsNullOrEmpty(portDesc))
            {
                await this.DeviceName.SetAsync("Select a device.");
            }
            else
            {
                await this.DeviceName.SetAsync($"Detecting ({portDesc})");
            }
            await this.DeviceState.SetAsync("Connecting...");

            try
            {
                if (settings.DeviceCategory == DeviceConstants.DeviceCategoryBT) // Bluetooth on multi-platform requires the use of a separtate library written in .NET core, so we have to special case it here.
                {
                    newDevice = await BluetoothDeviceFactory.CreateBluetoothDevice(settings.DeviceNameOrPort, this.logger);
                }
                else
                {
                    newDevice = DeviceFactory.CreateDevice(this.logger, settings.DeviceCategory, settings.DeviceNameOrPort);
                }
            }
            catch (TimeoutException)
            {
                return (null, null);
            }
            if (newDevice == null)
            {
                return (null, null);
            }

            if (await newDevice.Initialize())
                this.device = newDevice;
        }

        if (this.device == null)
        {
            return (null, null);
        }
        await this.DeviceName.SetAsync($"{this.device.GetDeviceType()}({portDesc})");
        ToolPresentNotifier notifier = new ToolPresentNotifier(this.device, this.protocol, this.logger);
        string basePath = string.Empty; // We will need to pass along a path to target kernel; Android won't path to a proper directory with GetExecutingAssembly().Location.
#if WINDOWS
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            basePath = Path.GetDirectoryName(exePath);
#elif ANDROID
            basePath = "/storage/emulated/0/PCMHammer/Bins";
#endif
        Vehicle newVehicle = new Vehicle(this.device, this.protocol, this.logger, notifier, basePath); // Kernel will use old logic on presence of empty string.
        await this.ConnectionState.SetAsync(ConnectionStates.Connecting);
        return (this.device, newVehicle);
    }

    // 

    /// <summary>
    /// Acquire a lease on the Vehicle object and underlying connection.
    /// </summary>
    /// <remarks>
    /// The public BeginActivity just ensures that we have a connection, and
    /// exclusive ownership of the state-change semapore. The real work happens
    /// in BeginActivityInternal.
    /// </remarks>
    /// <param name="activity">The name of the activity, for the UI and debug log.</param>
    /// <param name="canInterrupt">If true, this is a background activity (polling)
    /// so the user can interrupt it. If false, the activity must not be interrupted.</param>
    /// <returns>A lease on the connection. The activity will be ended when the lease
    /// object is Dispose()d.</returns>
    /// <exception cref="ConnectionUnavailableException"></exception>
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

        if (activity.StartsWith("Resetting"))
        {
            nextState = ConnectionStates.Polling;
        }

        // This will wait for any in-progress operations to complete, then acquire the semaphore.
        await this.stateChangeSemaphore.WaitAsync();

        try
        {
            // We want to be connected in order to proceed. Retry for up to 5 seconds.
            try
            {
                int retries = 5000 / FastRetryPeriod;
                for (int attempt = 1; attempt < retries; attempt++)
                {
                    if ((this.vehicle != null) && (this.internalState == ConnectionStates.Connected))
                    {
                        break;
                    }

                    this.retryPeriod = FastRetryPeriod;
                    await Task.Delay(100);
                }
            }
            finally
            {
                this.retryPeriod = SlowRetryPeriod;
            }

            if (this.vehicle == null)
            {
                throw new ConnectionUnavailableException("Not connected.");
            }


            await this.BeginActivityInternal(activity, nextState);
        }
        catch (Exception)
        {
            // If an exception is thrown (e.g. because the connection can't be
            // acquired) the 'using' pattern won't call the Dispose method,
            // so the semaphore has to be released explicitly.
            this.stateChangeSemaphore.Release();
            return null;
        }

        return new ConnectionLease(this, this.vehicle, activity);
    }

    private async Task BeginActivityInternal(string activity, ConnectionStates desiredState)
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

                this.logger.AddUserMessage("Beginning activity: " + activity);
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
                    this.logger.AddDebugMessage($"Skipping poll, internalState is {this.internalState}");
                    if(ResetTimeRemaining == -1)
                        throw new ConnectionUnavailableException("Unable to poll. " + errorMessage);
                }
                break;

            // The code that tests new connection settings uses BeginActivity
            // to ensure that it doesn't interrupt other activities.
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

    public async Task EndActivity(bool isConnected)
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
        }
        catch (Exception exception)
        {
            this.logger.AddDebugMessage("Exception in ConnectionService.EndActivity: " + exception.ToString());
            this.ForceTransition(ConnectionStates.NotConnected);
        }
        finally
        {
            await this.Activity.SetAsync(String.Empty);
            this.stateChangeSemaphore.Release();
        }

        this.StartTimer(null);
    }

    private void StartTimer(object? state)
    {
        this.timer = new System.Threading.Timer(
            TimerCallback,
            state: state,
            dueTime: this.retryPeriod,
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

        if (this.device == null && this.internalState == ConnectionStates.Connected)
        {
            ForceTransition(ConnectionStates.NotConnected);
        }
        bool disconnected = false;
        Vehicle? acquiredVehicle = null;
        try
        {
            // Suppress logging of timer callbacks, because they overwhelm the log.
            // TODO: accumulate logs in a circular buffer and keep them if an exception is thrown.
            // Meanwhile we re-enable the log in the catch blocks, both here and in TryPollOnce. (Which is kind of hacky, I'll admit.)
            this.logBuffer.Enabled = false;

            // This log line made more sense before logging was disabled in this scenario...
            this.logger.AddDebugMessage($"ConnectionService timer callback. Internal state: {this.internalState}.");

            // Re-create the connection if the settings have changed.
            if (this.newSettings != null && this.newSettings != this.lastSettings)
            {
                // This will call TryPollOnce, and will return true if that succeeds.
                // It will also update this.lastSettings when it succeeds.
                if (await this.TryConnect(this.newSettings))
                {
                    this.logger.AddUserMessage("Connected with new settings.");
                }
                else
                {
                    this.logger.AddUserMessage("Unable to connect with new settings.");
                    return;
                }
            }

            // Re-create the connection if the connection was lost.
            if (this.internalState == ConnectionStates.NotConnected && this.lastSettings != null)
            {
                if (await this.TryConnect(this.lastSettings))
                {
                    this.logger.AddUserMessage("Re-connected with current settings.");
                }
                else
                {
                    this.logger.AddUserMessage("Unable to reconnect with current settings.");
                    return;
                }
            }
            using (ConnectionLease lease = await this.BeginActivity(PollingActivity, true))
            {
                if(lease == null)
                {
                    ForceTransition(ConnectionStates.NotConnected);
                    return;
                }
                acquiredVehicle = lease.Vehicle;
                if (acquiredVehicle == null)
                {
                    return;
                }

                bool success = await this.TryPollOnce(acquiredVehicle);
                if (!success)
                {
                    this.internalState = ConnectionStates.NotConnected;

                    // This only affects the debug message written at the end of this method.
                    disconnected = true;

                    // This will cause EndActivity to transition to NotConnected.
                    if(ResetTimeRemaining != -1)
                        lease.ConnectionLost = true;
                }
            }
        }
        catch (ConnectionUnavailableException)
        {
            this.logger.AddDebugMessage("Poll skipped.");
        }
        catch (Exception exception)
        {
            this.logBuffer.Enabled = true;
            this.logger.AddDebugMessage("Error in timer callback: " + exception.ToString());
            disconnected = true;
        }
        finally
        {
            if (acquiredVehicle != null)
            {
                string result = disconnected ? "but disconnected" : "and still connected";
                this.logger.AddDebugMessage($"Exiting timer callback, vehicle acquired {result}");
            }
            else
            {
                this.logger.AddDebugMessage("Exiting timer callback, vehicle not acquired.");
            }

            this.logBuffer.Enabled = true;
        }
    }

    private async Task<bool> TryPollOnce(Vehicle vehicle)
    {
        bool success = false;

        using (var source = new CancellationTokenSource())
        {
            try
            {
                if(ResetTimeRemaining != -1)
                {
                    return true;
                }
                success = await TimeoutUtilities.TaskWithTimeoutAndException(
                    this.TryRequestVehicleInfo(vehicle, source.Token),
                    TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                this.logBuffer.Enabled = true;
                source.Cancel();
                success = false;
                this.logger.AddUserMessage("Connection test did not get a response from the vehicle.");
            }
            catch (Exception exception)
            {
                this.logBuffer.Enabled = true;
                this.logger.AddUserMessage("Error while testing vehicle connection.");
                this.logger.AddDebugMessage(exception.ToString());
                if(this.vehicle != null)
                {
                    this.vehicle?.Dispose();
                    this.vehicle = null;
                }
            }
        }

        return success;
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

        // Sanity check. This should never happen, but it does happen if you break in the debugger for a while.
        string activityName = await this.Activity.Value(cancellationToken) ?? string.Empty;
        if ((activityName != PollingActivity) && (activityName != TestingActivity))
        {
            return false;
        }

        try
        {
            if(await this.OperatingSystemId.Value() == _recoveryString || await this.OperatingSystemId.Value() == _kernelString)
            {
                await this.OperatingSystemId.SetAsync(string.Empty);
            }
            this.logger.AddUserMessage("Checking for a recovery message...");
            Response<bool> recoveryResponse = await vehicle.CheckForRecoveryMode(cancellationToken);
            if (recoveryResponse.Status == ResponseStatus.Success && recoveryResponse.Value == true)
            {
                this.logger.AddUserMessage("PCM/ECM recovery mode detected!");
                await this.OperatingSystemId.SetAsync(_recoveryString);
                return true;
            }
            this.logger.AddUserMessage("No recovery message detected. Checking for live kernel...");
            uint ver = await vehicle.GetKernelVersion(maxRetries: 1);
            if (ver != 0)
            {
                this.logger.AddUserMessage($"Detected kernel version: {ver}");
                await this.OperatingSystemId.SetAsync(_kernelString);
                return true;
            }
            await this.OperatingSystemId.SetAsync(string.Empty);
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
            this.logger.AddUserMessage("Communications exception: " + exception.Message);
            if(exception is InvalidOperationException || exception is IOException) 
            {
                try
                {
                    this.device?.Dispose();
                }
                catch { }
                this.device = null;
            }
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

    /// <summary>
    /// Transition from one of the expected states to the desired new state.
    /// </summary>
    /// <remarks>
    /// This was written before the semaphore was added. It's probably overkill
    /// now that the semaphore is enforcing state transitions.
    /// </remarks>
    private bool TryTransition(ConnectionStates expected, ConnectionStates newState)
    {
        this.logger.AddDebugMessage($"Transition requested from: {this.internalState}, to: {newState}"); 
        if (ResetTimeRemaining != -1 && newState > ConnectionStates.Connected)
        {
            this.logger.AddDebugMessage($"Transition denied due to ECM/PCM reset, staying in: {this.internalState}");
            return false;
        }
        if (this.internalState == ConnectionStates.Active && newState == ConnectionStates.Connected)
        {
            this.vehicle?.ExitKernel().Wait();
            this.vehicle?.ClearTroubleCodes().Wait();
            _leftActiveState = DateTime.Now;
        }
        if (((this.internalState & expected) > 0) || this.internalState == newState && ResetTimeRemaining == -1)
        {
            this.ForceTransition(newState);
            return true;
        }
        this.logger.AddDebugMessage($"Transition denied, staying in: {this.internalState}");
        return false;
    }

    private void ForceTransition(ConnectionStates newState)
    {
        this.internalState = newState;
        this.logger.AddDebugMessage($"Transitioned to: {newState}");
    }
}
