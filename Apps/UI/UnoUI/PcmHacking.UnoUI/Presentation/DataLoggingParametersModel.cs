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

    public ParameterEditContext(ParameterDatabase database, uint osid, LogColumn? logColumn)
    {
        this.Database = database;
        this.Osid = osid;
        this.Input = logColumn;
    }
}

public partial record DataLoggingParametersModel
{
    private const string StartRecordingButtonText = "Start Recording";
    private const string StopRecordingButtonText = "Stop Recording";

    private readonly LoggerAdapter progressLogger;
    private readonly ILogBuffer logBuffer;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly LoggingContext loggingContext;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;

    private string canPortName;    
    private CanLogger? canLogger;
    private ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>> logRowQueue = new ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>>();
    private ManualResetEvent exitWaitHandle = new ManualResetEvent(false);
    private AutoResetEvent rowAvailableHandle = new AutoResetEvent(false);
    private BackgroundWorker worker = new BackgroundWorker();
    private ParameterEditContext? editContext;
    private WriteState writeState = WriteState.None;

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
                    null);

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
                    logColumn);

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

    private async Task<Tuple<LogFileWriter, StreamWriter>> StartRecording(Logger logger)
    {
        await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StopRecordingButtonText);

        string profileName = Path.GetFileNameWithoutExtension(this.loggingContext.ProfilePath);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        string outputFileName = $"{timestamp}_{profileName}.csv";
        string path = Path.Combine(this.settingsService.GetDataLogFolder(), outputFileName);

        var streamWriter = new StreamWriter(path);
        var logFileWriter = new LogFileWriter(streamWriter);
        await logFileWriter.WriteHeader(logger.GetColumnNames());

        // TODO: make LogFileWriter respondible for disposing the StreamWriter, so this
        // can just return the LogFileWriter. Might wait until after the .Net 8 / Uno
        // branch becomes the main branch, to avoid complicating things.
        // TODO: Why isn't the tuple syntax working?
        return new Tuple<LogFileWriter, StreamWriter>(logFileWriter, streamWriter);
    }

    private async Task StopRecording(StreamWriter? streamWriter)
    {
        streamWriter?.Dispose();
        await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StartRecordingButtonText);
    }

    private void DataLoggingEditPage_OnApply(object? sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    private async Task OpenProfile()
    {
        try
        {
            await this.RecordingButtonText.SetAsync(DataLoggingParametersModel.StartRecordingButtonText);
            using (var lease = await this.connectionService.BeginActivity("Logging", true))
            using (new AwayMode())
            {
                if (lease == null)
                {
                    this.progressLogger.AddUserMessage("No vehicle connected.");
                    return;
                }

                var vehicle = lease.Vehicle;


                // Create the CAN logger.
                this.canLogger = new CanLogger(this.loggingContext.ParameterDatabase);

                if (string.IsNullOrEmpty(this.canPortName))
                {
                    await this.canLogger.SetPort(null);
                }
                else
                {
                    await this.canLogger.SetPort(new StandardPort(canPortName));

                }

                Logger? logger = null;

                // TODO: Write debug logs to a circular buffer instead of disabling it entirely.
                // ...and just append the last ~50 debug logs when logging is re-enabled.
                this.logBuffer.Enabled = false;
                LogFileWriter? logFileWriter = null;
                StreamWriter? streamWriter = null;

                while (!this.exitWaitHandle.WaitOne(0))
                {
                    if (this.editContext != null)
                    {
                        this.loggingContext.LogProfile = UpdateLogProfile();

                        // We only need to process the edit once, so we set this to null now.
                        this.editContext = null;

                        // This lets the data logging menu page know that the profile has been modified.
                        DataLoggingModel.ModifiedLoggingContext = this.loggingContext;

                        // This forces the logger to be re-created with the new profile.
                        logger = null;
                    }

                    if (logger == null)
                    {
                        logger = await InitializeLogger(vehicle, this.loggingContext.LogProfile, canLogger);
                        if (logger == null)
                        {
                            await Task.Delay(250);
                            continue;
                        }
                    }

                    switch(this.writeState)
                    {
                        case WriteState.StartWriting:
                            var tuple = await this.StartRecording(logger);
                            logFileWriter = tuple.Item1;
                            streamWriter = tuple.Item2;
                            this.writeState = WriteState.Writing;
                            await this.RecordingButtonEnabled.SetAsync(true);
                            break;

                        case WriteState.StopWriting:
                            await this.StopRecording(streamWriter);
                            streamWriter = null;
                            logFileWriter = null;
                            this.writeState = WriteState.None;
                            await this.RecordingButtonEnabled.SetAsync(true);
                            break;
                    }

                    IEnumerable<string> rowValues = await logger.GetNextRow();
                    if (rowValues != null)
                    {
                        await this.Rows.SetAsync(new LogRowValues(rowValues));

                        if (logFileWriter != null)
                        {
                            logFileWriter.WriteLine(rowValues);
                        }
                    }
                }
                this.logBuffer.Enabled = true;

                this.progressLogger.AddDebugMessage("DataLoggingParametersModel stopped logging.");
            }
        }
        catch (Exception ex)
        {
            this.logBuffer.Enabled = true;
            this.ProgressLogger.AddDebugMessage("Data logging exception: " + ex.Message);
            if (!this.exitWaitHandle.WaitOne(0))
            {
                this.dispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(100);
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

        try
        {
            await logger.StartLogging();
            this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");

            // This tells the view to prepare to render live data.
            await this.LoggerWrapper.SetAsync(new LoggerWrapper(logger), CancellationToken.None);

            // TODO: Wait for a signal from the view code instead using a fixed delay.
            await Task.Delay(100);

            await this.RecordingButtonEnabled.SetAsync(true);
            this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");
            return logger;
        }
        catch (Exception exception)
        {
            await this.RecordingButtonEnabled.SetAsync(false);
            this.progressLogger.AddDebugMessage("DataLoggingParametersModel unable to start logging.");
            this.progressLogger.AddDebugMessage(exception.ToString());

            // This tells the view to show an error message instead of live data.
            await this.DisplayErrorMessage(
                "Unable to start logging: " + 
                Environment.NewLine + 
                exception.Message +
                Environment.NewLine +
                "Will try again... ");
            return null;
        }
    }

    private async Task DisplayErrorMessage(string message)
    {
        await this.ErrorMessage.SetAsync(message);
        this.progressLogger.AddUserMessage("Unable to start logging.");
        this.progressLogger.AddDebugMessage(message);
    }

    public void StopLogging()
    {
        this.exitWaitHandle.Set();
    }
}
