using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using Uno.Extensions;
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

public partial record DataLoggingParametersModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private ISettingsService settingsService;
    private PcmHacking.ILogger progressLogger;
    private DispatcherQueue dispatcherQueue;
    private string profilePath;
    private string canPortName;
    private CanLogger? canLogger;
    private ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>> logRowQueue = new ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>>();
    private ManualResetEvent exitWaitHandle = new ManualResetEvent(false);
    private AutoResetEvent rowAvailableHandle = new AutoResetEvent(false);
    private BackgroundWorker worker = new BackgroundWorker();

    public PcmHacking.ILogger ProgressLogger { get { return this.progressLogger; } }
    public ManualResetEvent InitializationEvent { get; private set; }

    public IState<string> ErrorMessage => State<string>.Empty(this);

    public DataLoggingParametersModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        PcmHacking.ILogger progressLogger,
        DispatcherQueue dispatcherQueue,
        string profilePath)
    {
        this.navigator = navigator;
        this.connectionService = connectionService;
        this.settingsService = settingsService;
        this.progressLogger = progressLogger;
        this.dispatcherQueue = dispatcherQueue;
        this.profilePath = profilePath;
        this.canPortName = settingsService.GetCanSerialPortName();

        this.InitializationEvent = new ManualResetEvent(false);
        worker.DoWork += async (sender, e) => await this.OpenProfile();
        worker.RunWorkerAsync();
    }

    public IState<LoggerWrapper> LogProfile => State<LoggerWrapper>.Empty(this);
    public IState<LogRowValues> Rows => State<LogRowValues>.Empty(this);

    private async Task OpenProfile()
    {
        try
        {
            using (var lease = await this.connectionService.BeginActivity("Logging", true))
            {
                if (lease == null)
                {
                    this.progressLogger.AddUserMessage("No vehicle connected.");
                    return;
                }

                var vehicle = lease.Vehicle;

                // Load the database
                string appDirectory = AppContext.BaseDirectory;
                var database = new ParameterDatabase(appDirectory);
                database.LoadDatabase();

                // Create the log profile
                uint osid = 0;
                try
                {
                    var osidQueryResult = await vehicle.QueryOperatingSystemId(CancellationToken.None);
                    osid = osidQueryResult.Value;
                }
                catch (Exception ex)
                {
                    await this.DisplayErrorMessage("Unable to query the operating system ID: " + Environment.NewLine + ex.Message);
                    return;
                }

                LogProfileReader reader = new LogProfileReader(database, osid, this.progressLogger);
                var profile = reader.Read(this.profilePath);

                // This tells the view to update the UI with the new profile.
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

                Logger logger = vehicle.CreateLogger(osid, canLogger, profile.Columns, this.progressLogger);

                // Wait until the Page is ready.
                this.InitializationEvent.WaitOne();
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel initialization unblocked.");
                await this.LogProfile.SetAsync(new LoggerWrapper(logger), CancellationToken.None);
                await Task.Delay(100);
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel registered profile.");

                try
                {
                    await logger.StartLogging();
                    this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");
                }
                catch (Exception ex)
                {
                    await this.DisplayErrorMessage("Unable to start logging: " + Environment.NewLine + ex.Message);
                    return;
                }

                while (!this.exitWaitHandle.WaitOne(0))
                {
                    IEnumerable<string> rowValues = await logger.GetNextRow();
                    if (rowValues != null)
                    {
                        await this.Rows.SetAsync(new LogRowValues(rowValues));
                        // TODO: write data to disk
                    }
                }

                this.progressLogger.AddDebugMessage("DataLoggingParametersModel stopped logging.");
            }
        }
        catch (Exception ex)
        {
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
