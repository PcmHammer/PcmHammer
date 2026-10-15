// SPDX-License-Identifier: GPL-3.0-only
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
    public LogSession Session { get; private set; }

    /// <summary>The PCM source's logger, for the grid, which is laid out from the DPID groups.</summary>
    public Logger? Logger { get; private set; }

    /// <summary>The auxiliary bus, for the grid's broadcast rows.</summary>
    public AuxiliaryBusLogger AuxiliaryBusLogger { get; private set; }

    public LoggerWrapper(LogSession session, Logger? logger, AuxiliaryBusLogger auxiliaryBusLogger)
    {
        this.Session = session;
        this.Logger = logger;
        this.AuxiliaryBusLogger = auxiliaryBusLogger;
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

    // This one works on my car, but it might ONLY work on drive-by-wire cars.
    private const string AcceleratorPedalParameterId1 = "AcceleratorPedalPosition";
    // Ths one always reads zero on my car - might only work on cable-throttle cars.
    private const string AcceleratorPedalParameterId2 = "AcceleratorPedal";
    // Not tested yet.
    private const string KnockRetardParameterId = "KnockRetardDegrees";
    // Proven. But note that the cruise control must be enabled, which means we can either use
    // the cruise set button OR the cruise enable switch - but not both. I chose the button,
    // because it's much easier to operate without taking eyes off the road or hands off the
    // wheel.
    private const string CruiseSetCoastSwitchParameterId = "CruiseSetCoastSwitch";

    private readonly LoggerAdapter progressLogger;
    private readonly ILogBuffer logBuffer;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly LoggingContext loggingContext;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;

    public IState<bool> UseAcceleratorToSaveLogsEnabled => State<bool>
        .Value(this, () => false);
    public IState<bool> UseAcceleratorToSaveLogsChecked => State<bool>
        .Value(this, () => this.settingsService.GetUseAcceleratorToSaveLogs())
        .ForEach(SetUseAcceleratorToSaveLogs);
    public IState<bool> UseKnockRetardToSaveLogsEnabled => State<bool>
        .Value(this, () => false);
    public IState<bool> UseKnockRetardToSaveLogsChecked => State<bool>
        .Value(this, () => this.settingsService.GetUseKnockRetardToSaveLogs())
        .ForEach(SetUseKnockRetardToSaveLogs);
    public IState<bool> UseCruiseButtonToSaveLogsEnabled => State<bool>
        .Value(this, () => false);
    public IState<bool> UseCruiseButtonToSaveLogsChecked => State<bool>
        .Value(this, () => this.settingsService.GetUseCruiseButtonToSaveLogs())
        .ForEach(SetUseCruiseButtonToSaveLogs);

    public IState<string> ErrorMessage => State<string>.Empty(this);

    public IState<string> RecordingButtonText => State<string>.Value(this, () => DataLoggingParametersModel.StartRecordingButtonText);

    public IState<bool> RecordingButtonEnabled => State<bool>.Value(this, () => false);

    private AuxiliaryBusLogger? auxiliaryBusLogger;
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

    // Buffer for pre-trigger log rows (2 seconds worth)
    private PcmHacking.CircularBuffer<IEnumerable<string>> preTriggerBuffer;
    private const int PreTriggerBufferSeconds = 2;
    private const int EstimatedSamplingRate = 10; // rows per second, adjust as needed
    
    public LoggerAdapter ProgressLogger { get { return this.progressLogger; } }
    public ManualResetEvent InitializationEvent { get; private set; }

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

        // Buffer for 2 seconds of pre-trigger data
        preTriggerBuffer = new PcmHacking.CircularBuffer<IEnumerable<string>>(PreTriggerBufferSeconds * EstimatedSamplingRate);

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
            // TODO: For auxiliary bus parameters, LogColumn will be null and BusParameter will be valid.
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

    private async Task StartRecording(LogSession logger)
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
        LogSession? logger = null;
        try
        {
            await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StartRecordingButtonText);
            using (var lease = await this.connectionService.BeginActivity("Logging", true))
            using (new AwayMode())
            {
                while (!this.exitWaitHandle.WaitOne(0))
                {
                    try
                    {
                        var vehicle = lease.Vehicle;

                        // The auxiliary bus reads broadcast parameters from a second interface. This
                        // front end does not offer one yet, so the logger is created without a
                        // device and simply contributes no columns.
                        if (this.auxiliaryBusLogger == null)
                        {
                            this.auxiliaryBusLogger = new AuxiliaryBusLogger(
                                this.loggingContext.ParameterDatabase, this.progressLogger);

                            await this.auxiliaryBusLogger.Start(null, BusProtocol.VPW);
                        }

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
                                InitializeLogger(vehicle, this.loggingContext.LogProfile, this.auxiliaryBusLogger),
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
                                foreach (var bufferedRow in preTriggerBuffer)
                                {
                                    this.logFileWriter?.WriteLine(bufferedRow);
                                }

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
                            logger.Read(CancellationToken.None),
                            TimeSpan.FromSeconds(2));

                        await this.UpdateRecordingOptions(row);

                        IEnumerable<string> rowValues = row.Select(x => x.ValueAsString);

                        if (rowValues != null)
                        {
                            await this.Rows.SetAsync(new LogRowValues(rowValues));

                            // Add the row to the pre-trigger buffer.
                            this.preTriggerBuffer.Add(rowValues);
 
                            // Add the row to the output file, if we're writing to one.
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
                        this.progressLogger.AddDebugMessage("DataLoggingParametersModel unable to log.");
                        this.progressLogger.AddDebugMessage(exception.ToString());

                        // This tells the view to show an error message instead of live data.
                        await this.DisplayErrorMessage(exception.Message);
                        await Task.Delay(100);
                        await lease.Reconnect();
                        this.auxiliaryBusLogger?.Dispose();
                        this.auxiliaryBusLogger = null;
                        logger = null;
                    }
                } // the loop
                this.logBuffer.Enabled = true;
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel stopped logging.");
            } // ConnectionLease.Dispose is invoked here, on the way out of the 'using' block.
        }
        finally
        {
            this.logBuffer.Enabled = true;
            this.auxiliaryBusLogger?.Dispose();
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

    private async Task<LogSession?> InitializeLogger(
        Vehicle vehicle, LogProfile currentProfile, AuxiliaryBusLogger auxiliaryBusLogger)
    {
        // Source order is column order: the PCM, then the auxiliary bus.
        PcmParameterSource pcmSource = new PcmParameterSource(
            vehicle, this.loggingContext.OperatingSystemId, this.progressLogger);

        LogSession session = new LogSession(
            new IParameterSource[]
            {
                pcmSource,

                // No auxiliary interface in this front end, so no columns that could never fill.
                new AuxiliaryBusParameterSource(auxiliaryBusLogger, enabled: false),
            });

        // Wait until the Page is ready.
        this.InitializationEvent.WaitOne();
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel initialization unblocked.");

        // Opening configures the PCM, so the grid is now laid out from what it accepted rather than
        // from what was asked for. Parameters it refused are no longer shown as empty rows.
        if (!await session.Open(currentProfile.Columns.ToList(), CancellationToken.None))
        {
            session.Dispose();
            return null;
        }

        await session.Start(CancellationToken.None);

        // This tells the view to prepare to render live data.
        await this.LoggerWrapper.SetAsync(
            new LoggerWrapper(session, pcmSource.Logger, auxiliaryBusLogger), CancellationToken.None);

        // This forces the grid to re-render.
        var placeholderValues = new string[this.loggingContext.LogProfile.Columns.Count()];
        await this.Rows.SetAsync(new LogRowValues(placeholderValues));

        this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");

        // TODO: Wait for a signal from the view code instead using a fixed delay.
        await Task.Delay(250);

        await this.RecordingButtonEnabled.SetAsync(true);
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");
        return session;
    }

    private async Task UpdateAutoSaveCheckboxes(LogProfile profile)
    {
        bool throttlePresent = false;
        bool knockRetardPresent = false;
        bool cruiseButtonPresent = false;

        foreach (var column in profile.Columns)
        {
            switch (column.Parameter.Id)
            {
                case AcceleratorPedalParameterId1:
                case AcceleratorPedalParameterId2:
                    throttlePresent = true;
                    break;

                case KnockRetardParameterId:
                    knockRetardPresent = true;
                    break;

                case CruiseSetCoastSwitchParameterId:
                    cruiseButtonPresent = true;
                    break;
            }
        }

        await this.UseAcceleratorToSaveLogsEnabled.SetAsync(throttlePresent);
        await this.UseKnockRetardToSaveLogsEnabled.SetAsync(knockRetardPresent);
        await this.UseCruiseButtonToSaveLogsEnabled.SetAsync(cruiseButtonPresent);
    }

    private async Task UpdateRecordingOptions(IEnumerable<LogRowElement> row)
    {
        bool useAcceleratorToSaveLogsChecked = await this.UseAcceleratorToSaveLogsChecked.Value();
        bool useKnockRetardToSaveLogsChecked = await this.UseKnockRetardToSaveLogsChecked.Value();
        bool useCruiseButtonToSaveLogsChecked = await this.UseCruiseButtonToSaveLogsChecked.Value();

        bool autoSaveEnabled = useAcceleratorToSaveLogsChecked ||
            useKnockRetardToSaveLogsChecked ||
            useCruiseButtonToSaveLogsChecked;

        bool buttonEnabled = await this.RecordingButtonEnabled.Value();

        if (autoSaveEnabled && buttonEnabled) {
            await this.RecordingButtonText.SetAsync("Auto");
            await this.RecordingButtonEnabled.SetAsync(false);
        }
        else if (!autoSaveEnabled && !buttonEnabled)
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
        
        if (!autoSaveEnabled)
        {
            return;
        }

        bool isWriting = this.writeState == WriteState.Writing || this.writeState == WriteState.StartWriting;
        bool shouldBeWriting = false;
        foreach(var element in row)
        {
            if (useAcceleratorToSaveLogsChecked && (element.ParameterId == AcceleratorPedalParameterId1 || element.ParameterId == AcceleratorPedalParameterId2))
            {
                shouldBeWriting |= element.ValueAsNumber > 80;
            }

            if (useKnockRetardToSaveLogsChecked && element.ParameterId == KnockRetardParameterId)
            {
                shouldBeWriting |= element.ValueAsNumber > 0;
            }

            if (useCruiseButtonToSaveLogsChecked && element.ParameterId == CruiseSetCoastSwitchParameterId)
            {
                shouldBeWriting |= element.ValueAsString == "Pressed";
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

    private ValueTask SetUseAcceleratorToSaveLogs(bool value, CancellationToken ct)
    {
        this.settingsService.SetUseAcceleratorToSaveLogs(value);
        return ValueTask.CompletedTask;
    }

    private ValueTask SetUseCruiseButtonToSaveLogs(bool value, CancellationToken ct)
    {
        this.settingsService.SetUseCruiseButtonToSaveLogs(value);
        return ValueTask.CompletedTask;
    }

    private ValueTask SetUseKnockRetardToSaveLogs(bool value, CancellationToken ct)
    {
        this.settingsService.SetUseKnockRetardToSaveLogs(value);
        return ValueTask.CompletedTask;
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
