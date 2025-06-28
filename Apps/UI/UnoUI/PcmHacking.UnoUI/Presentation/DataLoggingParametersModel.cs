using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions;
using Uno.UI.Extensions;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.UI.Notifications;

namespace PcmHacking.UnoUI.Presentation;

public enum WriteState
{
    None,
    StartWriting,
    Writing,
    StopWriting
}

public class LoggingContext
{
    public LogProfile LogProfile { get; set; }
    public string ProfilePath { get; private set; }
    public uint OperatingSystemId { get; private set; }
    public ParameterDatabase ParameterDatabase { get; private set; }

    public LoggingContext(LogProfile logProfile, string profilePath, uint operatingSystemId, ParameterDatabase parameterDatabase)
    {
        LogProfile = logProfile;
        ProfilePath = profilePath;
        OperatingSystemId = operatingSystemId;
        ParameterDatabase = parameterDatabase;
    }
}

public struct LogRowValues
{
    public IEnumerable<string> Values { get; private set; }
    public LogRowValues(IEnumerable<string> values)
    {
        this.Values = values;
    }
}

public struct LoggerWrapper
{
    public Logger Logger { get; private set;  }

    public LoggerWrapper(Logger logger)
    {
        this.Logger = logger;
    }
}

public class ParameterEditContext
{
    public ParameterDatabase Database { get; private set; }
    public uint Osid { get; private set; }
    public LogColumn? Input { get; private set; }
    public LogColumn? Output { get; set; }
    public LogProfile LogProfile { get; private set; } // Added LogProfile property

    public ParameterEditContext(ParameterDatabase database, uint osid, LogColumn? logColumn, LogProfile logProfile)
    {
        this.Database = database;
        this.Osid = osid;
        this.Input = logColumn;
        this.LogProfile = logProfile; // Assign LogProfile
    }
}

public partial record DataLoggingParametersModel
{
    private const string StartRecordingButtonText = "Start Recording";
    private const string StopRecordingButtonText = "Stop Recording";

    private const string AcceleratorPedalParameterId = "AcceleratorPedal";
    private const string CruiseOnOffParameterId = "CruiseOnOffSwitch";
    private const string CruiseSetCoastSwitchParameterId = "CruiseSetCoastSwitch";

    private readonly LoggerAdapter progressLogger;
    private readonly ILogBuffer logBuffer;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly LoggingContext loggingContext;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;

    public IState<bool> AutoSaveThrottleEnabled => State<bool>.Value(this, () => false);
    public IState<bool> AutoSaveThrottleChecked => State<bool>.Value(this, () => false);
    public IState<bool> AutoSaveCruiseSwitchEnabled => State<bool>.Value(this, () => false);
    public IState<bool> AutoSaveCruiseSwitchChecked => State<bool>.Value(this, () => false);
    public IState<bool> AutoSaveCruiseButtonEnabled => State<bool>.Value(this, () => false);
    public IState<bool> AutoSaveCruiseButtonChecked => State<bool>.Value(this, () => false);

    private string canPortName;    
    private CanLogger? canLogger;
    private ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>> logRowQueue = new ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>>();
    private ManualResetEvent exitWaitHandle = new ManualResetEvent(false);
    private AutoResetEvent rowAvailableHandle = new AutoResetEvent(false);
    private BackgroundWorker worker = new BackgroundWorker();
    private ParameterEditContext? editContext;
    private WriteState writeState = WriteState.None;

    // Writes formatted low rows to an underlying StreamWriter.
    private LogFileWriter? logFileWriter = null;

    // The underlying StreamWriter for the LogFileWriter.
    private StreamWriter? streamWriter = null;

    public LoggerAdapter ProgressLogger { get { return this.progressLogger; } }
    public ManualResetEvent InitializationEvent { get; private set; }

    public IState<string> ErrorMessage => State<string>.Empty(this);

    public IState<string> RecordingButtonText => State<string>.Value(this, () => DataLoggingParametersModel.StartRecordingButtonText);

    public IState<bool> RecordingButtonEnabled => State<bool>.Value(this, () => false);

    public DataLoggingParametersModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter progressLogger,
        ILogBuffer logBuffer,
        DispatcherQueue dispatcherQueue,
        LoggingContext loggingContext)
    {
        this.navigator = navigator;
        this.connectionService = connectionService;
        this.settingsService = settingsService;
        this.progressLogger = progressLogger;
        this.logBuffer = logBuffer;
        this.dispatcherQueue = dispatcherQueue;
        this.loggingContext = loggingContext;
        this.canPortName = settingsService.GetCanSerialPortName();

        this.InitializationEvent = new ManualResetEvent(false);
        worker.DoWork += async (sender, e) => await this.OpenProfile();
        worker.RunWorkerAsync();
    }

    public IState<LoggerWrapper> LoggerWrapper => State<LoggerWrapper>.Empty(this);
    public IState<LogRowValues> Rows => State<LogRowValues>.Empty(this);

    [Command]
    public Task AddParameter()
    {
        this.dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                ParameterEditContext temporaryEditContext = new(
                    this.loggingContext.ParameterDatabase, 
                    this.loggingContext.OperatingSystemId, 
                    null,
                    this.loggingContext.LogProfile); // Pass LogProfile

                DataLoggingEditPage dataLoggingEditPage = new DataLoggingEditPage();
                dataLoggingEditPage.XamlRoot = XamlRootService.GetXamlRoot();
                dataLoggingEditPage.OnApply += (s, e) =>
                {
                    this.editContext = temporaryEditContext;
                };

                dataLoggingEditPage.DataContext = new DataLoggingEditViewModel(temporaryEditContext);
                await dataLoggingEditPage.ShowAsync();
            }
            catch (Exception exception)
            {
                exception.ToString();
            }
        });
        return Task.CompletedTask;
    }

    public async Task EditParameter(DataSource dataSource)
    {
        if (dataSource.LogColumn != null)
        {
            // TODO: For CAN parameters, LogColumn will be null and CanParameter will be valid.
            var logColumn = dataSource.LogColumn;
            var parameter = logColumn.Parameter;
            if (parameter != null)
            {
                ParameterEditContext temporaryEditContext = new(
                    this.loggingContext.ParameterDatabase, 
                    this.loggingContext.OperatingSystemId, 
                    logColumn,
                    this.loggingContext.LogProfile); // Pass LogProfile

                DataLoggingEditPage dataLoggingEditPage = new DataLoggingEditPage();
                dataLoggingEditPage.XamlRoot = XamlRootService.GetXamlRoot();
                dataLoggingEditPage.OnApply += (s, e) =>
                {
                    this.editContext = temporaryEditContext;
                };
                dataLoggingEditPage.OnDelete += async (s, e) =>
                {
                    var confirmationDialog = new ContentDialog
                    {
                        Title = "Are you sure?",
                        Content = $"Are you sure you want to stop logging the {logColumn.Parameter.Name} parameter?",
                        PrimaryButtonText = "Yes, remove it.",
                        SecondaryButtonText = "No, keep it.",
                    };
                    confirmationDialog.XamlRoot = XamlRootService.GetXamlRoot();
                    confirmationDialog.PrimaryButtonClick += (s, e) =>
                    {
                        temporaryEditContext.Output = null;
                        this.editContext = temporaryEditContext;                        
                    };
                    await confirmationDialog.ShowAsync();
                };

                dataLoggingEditPage.DataContext = new DataLoggingEditViewModel(temporaryEditContext);
                await dataLoggingEditPage.ShowAsync();
            }
        }
        else
        {
            // await this.navigator.NavigateToParameterEditor(dataSource);
        }
    }

    [Command]
    public async ValueTask StartStopRecording()
    {
        switch (this.writeState)
        {
            case WriteState.None:
                this.writeState = WriteState.StartWriting;
                break;

            case WriteState.Writing:
                this.writeState = WriteState.StopWriting;
                break;
        }
        await this.RecordingButtonEnabled.SetAsync(false);
    }

    private async Task StartRecording(Logger logger)
    {
        await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StopRecordingButtonText);

        string profileName = Path.GetFileNameWithoutExtension(this.loggingContext.ProfilePath);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        string outputFileName = $"{timestamp}_{profileName}.csv";
        string path = Path.Combine(this.settingsService.GetDataLogFolder(), outputFileName);

        this.streamWriter = new StreamWriter(path);
        this.logFileWriter = new LogFileWriter(streamWriter);
        await this.logFileWriter.WriteHeader(logger.GetColumnNames());
    }

    private async Task StopRecording()
    {
        // TODO: make LogFileWriter respondible for disposing the StreamWriter.
        // Might wait until after the .Net 8 / Uno branch becomes the main
        // branch, to avoid complicating things.
        this.logFileWriter = null;

        // Let's not dispose the underlying StreamWriter while the logger
        // is using it. This will cause the UI to hang for a moment. :(
        await Task.Delay(500);

        this.streamWriter?.Dispose();
        this.streamWriter = null;
        await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StartRecordingButtonText);
    }

    private void DataLoggingEditPage_OnApply(object? sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    private async Task OpenProfile()
    {
        Logger? logger = null;
        try
        {
            await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StartRecordingButtonText);
            using (var lease = await this.connectionService.BeginActivity("Logging", true))
            using (new AwayMode())
            {
                // This probably isn't needed anymore...
                if (lease == null)
                {
                    this.progressLogger.AddUserMessage("No vehicle connected.");
                    return;
                }

                var vehicle = lease.Vehicle;

                // Create the CAN logger.
                if (this.canLogger == null)
                {
                    this.canLogger = new CanLogger(this.loggingContext.ParameterDatabase, this.progressLogger);

                    if (string.IsNullOrEmpty(this.canPortName))
                    {
                        await this.canLogger.SetPort(null);
                    }
                    else
                    {
                        IPort canPort = new StandardPort(canPortName);
                        await this.canLogger.SetPort(canPort);
                    }
                }

                while (!this.exitWaitHandle.WaitOne(0))
                {
                    try
                    {
                        if (this.editContext != null)
                        {
                            this.loggingContext.LogProfile = this.UpdateLogProfile();

                            // We only need to process the edit once, so we set this to null now.
                            this.editContext = null;

                            // This lets the data logging menu page know that the profile has been modified.
                            DataLoggingModel.ModifiedLoggingContext = this.loggingContext;

                            // This forces the logger to be re-created with the new profile.
                            logger = null;
                        }


                        if (logger == null)
                        {
                            this.logBuffer.Enabled = true;

                            logger = await TimeoutUtilities.TaskWithTimeoutAndException(
                                InitializeLogger(vehicle, this.loggingContext.LogProfile, canLogger),
                                TimeSpan.FromSeconds(2));

                            await this.UpdateAutoSaveCheckboxes(this.loggingContext.LogProfile);

                            // TODO: Write debug logs to a circular buffer instead of disabling it entirely.
                            // ...and just append the last ~50 debug logs when debug logging is re-enabled.
                            this.logBuffer.Enabled = false;
                        }

                        if (logger == null)
                        {
                            await Task.Delay(500);
                            continue;
                        }

                        switch (this.writeState)
                        {
                            case WriteState.StartWriting:
                                await this.StartRecording(logger);
                                this.writeState = WriteState.Writing;
                                await this.RecordingButtonEnabled.SetAsync(true);
                                break;

                            case WriteState.StopWriting:
                                await this.StopRecording();
                                this.writeState = WriteState.None;
                                await this.RecordingButtonEnabled.SetAsync(true);
                                break;
                        }

                        IEnumerable<LogRowElement> row = await TimeoutUtilities.TaskWithTimeoutAndException(
                            logger.GetNextRowV2(),
                            TimeSpan.FromSeconds(2));

                        await this.UpdateRecordingOptions(row);

                        IEnumerable<string> rowValues = row.Select(x => x.ValueAsString);

                        if (rowValues != null)
                        {
                            await this.Rows.SetAsync(new LogRowValues(rowValues));

                            if (logFileWriter != null)
                            {
                                logFileWriter.WriteLine(rowValues);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        this.logBuffer.Enabled = true;
                        await this.RecordingButtonEnabled.SetAsync(false);
                        this.progressLogger.AddDebugMessage("DataLoggingParametersModel unable to start logging.");
                        this.progressLogger.AddDebugMessage(exception.ToString());

                        // This tells the view to show an error message instead of live data.
                        await this.DisplayErrorMessage(exception.Message);
                        await Task.Delay(500);
                    }
                }
                this.logBuffer.Enabled = true;
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel stopped logging.");
            } // ConnectionLease.Dispose is invoked here, on the way out of the 'using' block.
        }
        catch (Exception ex)
        {
            this.logBuffer.Enabled = true;
            this.ProgressLogger.AddDebugMessage("Data logging exception: " + ex.Message);
            if (!this.exitWaitHandle.WaitOne(0))
            {
                this.dispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(500);
                    worker.RunWorkerAsync();
                });
            }
            else
            {
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel stopped trying to connect.");
            }
        }
        finally
        {
            this.logBuffer.Enabled = true;
            this.canLogger?.Dispose();
            await this.StopRecording();
        }
    }

    private LogProfile UpdateLogProfile()
    {
        if (this.editContext == null)
        {
            this.progressLogger.AddDebugMessage("DataLoggingParametersModel.UpdateLogProfile: editContext is null.");
            return this.loggingContext.LogProfile;
        }

        // re-create the profile and the logger
        LogProfile newProfile = new LogProfile();

        // copy all the columns from the old profile, other than the deleted or edited column
        foreach (var column in this.loggingContext.LogProfile.Columns)
        {
            if (column.Parameter.Id != this.editContext.Input?.Parameter.Id)
            {
                newProfile.AddColumn(column);
            }
        }

        if (this.editContext.Output != null)
        {
            // add the new column
            newProfile.AddColumn(this.editContext.Output);
        }

        this.loggingContext.LogProfile = newProfile;

        // This will cause the DataLogging page to enable the save/save-as
        // buttons when the user navigates back. This seems hacky though.
        // TODO: What's the right way to communicate the state back to that page?
        DataLoggingModel.ModifiedLoggingContext = this.loggingContext;

        return newProfile;
    }

    private async Task<Logger?> InitializeLogger(Vehicle vehicle, LogProfile currentProfile, CanLogger canLogger)
    {
        Logger logger = vehicle.CreateLogger(this.loggingContext.OperatingSystemId, canLogger, currentProfile.Columns, this.progressLogger);

        // Wait until the Page is ready.
        this.InitializationEvent.WaitOne();
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel initialization unblocked.");

        // This tells the view to prepare to render live data.
        await this.LoggerWrapper.SetAsync(new LoggerWrapper(logger), CancellationToken.None);

        // This forces the grid to re-render. It's important to do this before
        // logging starts, so that any unsupported parameters will be visible,
        // so that the user can delete them.
        var placeholderValues = new string[this.loggingContext.LogProfile.Columns.Count()];
        await this.Rows.SetAsync(new LogRowValues(placeholderValues));

        // Request the first row of real data.
        await logger.StartLogging();
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");

        // TODO: Wait for a signal from the view code instead using a fixed delay.
        await Task.Delay(250);

        await this.RecordingButtonEnabled.SetAsync(true);
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");
        return logger;
    }

    private async Task UpdateAutoSaveCheckboxes(LogProfile profile)
    {
        bool throttlePresent = false;
        bool cruiseSwitchPresent = false;
        bool cruiseButtonPresent = false;

        foreach (var column in profile.Columns)
        {
            switch (column.Parameter.Id)
            {
                case AcceleratorPedalParameterId:
                    throttlePresent = true;
                    break;

                case CruiseOnOffParameterId:
                    cruiseSwitchPresent = true;
                    break;

                case CruiseSetCoastSwitchParameterId:
                    cruiseButtonPresent = true;
                    break;
            }
        }

        await this.AutoSaveThrottleEnabled.SetAsync(throttlePresent);
        if (!throttlePresent)
        {
            await this.AutoSaveThrottleChecked.SetAsync(false);
        }

        await this.AutoSaveCruiseSwitchEnabled.SetAsync(cruiseSwitchPresent);
        if (!cruiseSwitchPresent)
        {
            await this.AutoSaveCruiseSwitchChecked.SetAsync(false);
        }

        await this.AutoSaveCruiseButtonEnabled.SetAsync(cruiseButtonPresent);
        if (!cruiseButtonPresent)
        {
            await this.AutoSaveCruiseButtonChecked.SetAsync(false);
        }
    }

    private async Task UpdateRecordingOptions(IEnumerable<LogRowElement> row)
    {
        bool autoSaveThrottleChecked = await this.AutoSaveThrottleChecked.Value();
        bool autoSaveCruiseSwitchChecked = await this.AutoSaveCruiseSwitchChecked.Value();
        bool autoSaveCruiseButtonChecked = await this.AutoSaveCruiseButtonChecked.Value();

        bool autoEnabled = autoSaveThrottleChecked ||
            autoSaveCruiseSwitchChecked ||
            autoSaveCruiseButtonChecked;

        bool buttonEnabled = await this.RecordingButtonEnabled.Value();

        if (autoEnabled && buttonEnabled) {
            await this.RecordingButtonText.SetAsync("Auto");
            await this.RecordingButtonEnabled.SetAsync(false);
        }
        else if (!autoEnabled && !buttonEnabled)
        {
            // Choose the right text for the Start/Stop recording button.
            string buttonText;
            switch (this.writeState)
            {
                case WriteState.Writing:
                case WriteState.StopWriting:
                    buttonText = StartRecordingButtonText;
                    break;

                case WriteState.None:
                case WriteState.StartWriting:
                    buttonText = StopRecordingButtonText;
                    break;

                default:
                    buttonText = "Bug";
                    break;
            }

            await this.RecordingButtonText.SetAsync(buttonText);
        }
        
        if (!autoEnabled)
        {
            return;
        }

        bool isWriting = this.writeState == WriteState.Writing || this.writeState == WriteState.StartWriting;
        bool shouldBeWriting = false;
        foreach(var element in row)
        {
            if (autoSaveThrottleChecked && element.ParameterId == AcceleratorPedalParameterId)
            {
                shouldBeWriting = element.ValueAsNumber > 80;
            }

            if (autoSaveCruiseSwitchChecked && element.ParameterId == CruiseOnOffParameterId)
            {
                shouldBeWriting = element.ValueAsString == "On";
            }

            if (autoSaveCruiseButtonChecked && element.ParameterId == CruiseSetCoastSwitchParameterId)
            {
                shouldBeWriting = element.ValueAsString == "Pressed";
            }
        }

        if (!isWriting && shouldBeWriting)
        {
            this.writeState = WriteState.StartWriting;
        }

        if (isWriting && !shouldBeWriting)
        {
            this.writeState = WriteState.StopWriting;
        }
    }

    private async Task DisplayErrorMessage(string message)
    {
        await this.ErrorMessage.SetAsync(message);
        this.progressLogger.AddUserMessage("Unable to start logging.");
        this.progressLogger.AddDebugMessage(message);
    }

    /// <summary>
    /// Invoked by the UI when the user clicks the back-button.
    /// </summary>
    /// <remarks>
    /// Setting the handle causes the logging code to stop looping,
    /// then release the connection and clean everything up.
    /// </remarks>
    public void StopLogging()
    {
        this.exitWaitHandle.Set();
    }
}
