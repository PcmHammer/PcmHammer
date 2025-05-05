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
using Windows.Devices.Bluetooth.Advertisement;

namespace PcmHacking.UnoUI.Presentation;

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

public class DataLoggingEditContext
{
    public ParameterDatabase Database { get; private set; }
    public uint Osid { get; private set; }
    public LogColumn Input { get; private set; }
    public LogColumn? Output { get; set; }

    public DataLoggingEditContext(ParameterDatabase database, uint osid, LogColumn logColumn)
    {
        this.Database = database;
        this.Osid = osid;
        this.Input = logColumn;
    }
}

public partial record DataLoggingParametersModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private ISettingsService settingsService;
    private readonly LoggerAdapter progressLogger;
    private readonly ILogBuffer logBuffer;
    private readonly DispatcherQueue dispatcherQueue;
    private ParameterDatabase? database;
    private uint osid;
    private string profilePath;
    private string canPortName;    
    private CanLogger? canLogger;
    private ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>> logRowQueue = new ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>>();
    private ManualResetEvent exitWaitHandle = new ManualResetEvent(false);
    private AutoResetEvent rowAvailableHandle = new AutoResetEvent(false);
    private BackgroundWorker worker = new BackgroundWorker();
    private DataLoggingEditContext? editContext;

    public LoggerAdapter ProgressLogger { get { return this.progressLogger; } }
    public ManualResetEvent InitializationEvent { get; private set; }

    public IState<string> ErrorMessage => State<string>.Empty(this);

    public DataLoggingParametersModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter progressLogger,
        ILogBuffer logBuffer,
        DispatcherQueue dispatcherQueue,
        string profilePath)
    {
        this.navigator = navigator;
        this.connectionService = connectionService;
        this.settingsService = settingsService;
        this.progressLogger = progressLogger;
        this.logBuffer = logBuffer;
        this.dispatcherQueue = dispatcherQueue;
        this.profilePath = profilePath;
        this.canPortName = settingsService.GetCanSerialPortName();

        this.InitializationEvent = new ManualResetEvent(false);
        worker.DoWork += async (sender, e) => await this.OpenProfile();
        worker.RunWorkerAsync();
    }

    public IState<LoggerWrapper> LoggerWrapper => State<LoggerWrapper>.Empty(this);
    public IState<LogRowValues> Rows => State<LogRowValues>.Empty(this);

    public async Task EditParameter(DataSource dataSource)
    {
        if (this.database == null)
        {
            this.progressLogger.AddDebugMessage("this.database is null in DataLoggingParametersModel.EditParameter");
            return;
        }

        if (dataSource.LogColumn != null)
        {
            // TODO: For CAN parameters, LogColumn will be null and CanParameter will be valid.
            var logColumn = dataSource.LogColumn;
            var parameter = logColumn.Parameter;
            if (parameter != null)
            {
                DataLoggingEditContext temporaryEditContext = new(this.database, this.osid, logColumn);

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

    private void DataLoggingEditPage_OnApply(object? sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    private async Task OpenProfile()
    {
        try
        {
            using (var lease = await this.connectionService.BeginActivity("Logging", true))
            using (new AwayMode())
            {
                if (lease == null)
                {
                    this.progressLogger.AddUserMessage("No vehicle connected.");
                    return;
                }

                var vehicle = lease.Vehicle;

                // Load the database
                string appDirectory = AppContext.BaseDirectory;
                this.database = new ParameterDatabase(appDirectory);
                this.database.LoadDatabase();

                // Create the log profile
                try
                {
                    var osidQueryResult = await vehicle.QueryOperatingSystemId(CancellationToken.None);
                    this.osid = osidQueryResult.Value;
                }
                catch (Exception ex)
                {
                    await this.DisplayErrorMessage("Unable to query the operating system ID: " + Environment.NewLine + ex.Message);
                    return;
                }

                LogProfileReader reader = new LogProfileReader(database, this.osid, this.progressLogger);
                var currentProfile = reader.Read(this.profilePath);
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel loaded profile.");

                // Create the logger, and start logging.
                this.canLogger = new CanLogger(database);

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
                while (!this.exitWaitHandle.WaitOne(0))
                {
                    if (this.editContext != null)
                    {
                        currentProfile = UpdateLogProfile(currentProfile);

                        // We only need to process the edit once, so we set this to null now.
                        this.editContext = null;

                        // This forces the logger to be re-created with the new profile.
                        logger = null;
                    }

                    if (logger == null)
                    {
                        logger = await InitializeLogger(vehicle, currentProfile, canLogger);
                        if (logger == null)
                        {
                            await Task.Delay(250);
                            continue;
                        }
                    }

                    IEnumerable<string> rowValues = await logger.GetNextRow();
                    if (rowValues != null)
                    {
                        await this.Rows.SetAsync(new LogRowValues(rowValues));
                        // TODO: write data to disk
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

    private LogProfile UpdateLogProfile(LogProfile currentProfile)
    {
        if (this.editContext == null)
        {
            this.progressLogger.AddDebugMessage("DataLoggingParametersModel.UpdateLogProfile: editContext is null.");
            return currentProfile;
        }

        // re-create the profile and the logger
        LogProfile newProfile = new LogProfile();

        // copy all the columns from the old profile, other than the deleted or edited column
        foreach (var column in currentProfile.Columns)
        {
            if (column.Parameter.Id != this.editContext.Input.Parameter.Id)
            {
                newProfile.AddColumn(column);
            }
        }

        if (this.editContext.Output != null)
        {
            // add the new column
            newProfile.AddColumn(this.editContext.Output);
        }

        return newProfile;
    }

    private async Task<Logger?> InitializeLogger(Vehicle vehicle, LogProfile currentProfile, CanLogger canLogger)
    {
        Logger logger = vehicle.CreateLogger(this.osid, canLogger, currentProfile.Columns, this.progressLogger);

        // Wait until the Page is ready.
        this.InitializationEvent.WaitOne();
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel initialization unblocked.");

        // This tells the view to update the UI with the new profile.
        await this.LoggerWrapper.SetAsync(new LoggerWrapper(logger), CancellationToken.None);
        await Task.Delay(100);
        this.progressLogger.AddDebugMessage("DataLoggingParametersModel registered profile.");

        try
        {
            await logger.StartLogging();
            this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");
            return logger;
        }
        catch (Exception ex)
        {
            await this.DisplayErrorMessage("Unable to start logging: " + Environment.NewLine + ex.Message);
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
