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

public struct LogProfileWrapper
{
    public LogProfile Profile { get; private set;  }

    public LogProfileWrapper(LogProfile profile)
    {
        this.Profile = profile;
    }
}

public partial record DataLoggingParametersModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private ISettingsService settingsService;
    private PcmHacking.ILogger progressLogger;
    private string profilePath;
    private string canPortName;
    private CanLogger? canLogger;
    private ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>> logRowQueue = new ConcurrentQueue<Tuple<Logger, LogFileWriter?, IEnumerable<string>>>();
    private EventWaitHandle exitWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
    private EventWaitHandle rowAvailableHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
    private BackgroundWorker worker = new BackgroundWorker();

    public PcmHacking.ILogger ProgressLogger { get { return this.progressLogger; } }
    public ManualResetEvent InitializationEvent { get; private set; }

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
        this.profilePath = profilePath;
        this.canPortName = settingsService.GetCanSerialPortName();

        this.InitializationEvent = new ManualResetEvent(false);
        worker.DoWork += async (sender, e) => await this.OpenProfile();
        worker.RunWorkerAsync();
    }

    public IState<LogProfileWrapper> LogProfile => State<LogProfileWrapper>.Empty(this);
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
                    await this.ShowErrorMessage("Unable to query the operating system ID: " + ex.Message);
                    return;
                }

                LogProfileReader reader = new LogProfileReader(database, osid, this.progressLogger);
                var profile = reader.Read(this.profilePath);

                // This tells the view to update the UI with the new profile.
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel loaded profile.");
                this.InitializationEvent.WaitOne();
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel initialization unblocked.");
                await this.LogProfile.SetAsync(new LogProfileWrapper(profile), CancellationToken.None);
                await Task.Delay(100);
                this.progressLogger.AddDebugMessage("DataLoggingParametersModel registered profile.");

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
                try
                {
                    await logger.StartLogging();
                    this.progressLogger.AddDebugMessage("DataLoggingParametersModel started logging.");
                }
                catch (Exception ex)
                {
                    await this.ShowErrorMessage("Unable to start logging: " + ex.Message);
                    return;
                }

                while (!exitWaitHandle.WaitOne(0))
                {
                    IEnumerable<string> rowValues = await logger.GetNextRow();
                    if (rowValues != null)
                    {
                        await this.Rows.SetAsync(new LogRowValues(rowValues));

                        /*                    // Hand this data off to be written to disk and displayed in the UI.
                                            this.logRowQueue.Enqueue(
                                                new Tuple<Logger, LogFileWriter?, IEnumerable<string>>(
                                                    logger,
                                                    null, // file writer
                                                    rowValues));

                                            this.rowAvailableHandle.Set();
                        */
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.ProgressLogger.AddDebugMessage("Data logging exception: " + ex.Message);
            worker.RunWorkerAsync();
        }
    }

    private async Task ShowErrorMessage(string message)
    {
        // This is hacky but it dispays the error message...
        // TODO: hide the grid, show a white-on-red "danger to manifold" error message.
        var fakeProfile = new LogProfile();
        var fakeConversion = new Conversion(string.Empty, string.Empty, string.Empty);
        fakeProfile.AddColumn(
            new LogColumn(
                new PidParameter(
                    String.Empty,
                    message,
                    String.Empty,
                    "uint8",
                    false,
                    new Conversion[] { fakeConversion },
                    0,
                    new uint[0]),
                fakeConversion,
                false));
        await this.LogProfile.SetAsync(new LogProfileWrapper(fakeProfile), CancellationToken.None);

        this.progressLogger.AddUserMessage("Failed to start logging.");
        this.progressLogger.AddDebugMessage(message);
    }

    public void StopLogging()
    {
        this.exitWaitHandle.Set();
    }
    /*
    private async IAsyncEnumerable<LogRowValues> RowFactory([EnumeratorCancellation] CancellationToken ct)
    {
        WaitHandle[] waitHandles = new WaitHandle[] { exitWaitHandle, ct.WaitHandle, rowAvailableHandle };

        while (true)
        {
            int index = await Task.Run(() => WaitHandle.WaitAny(waitHandles));
            if (index == 0 || index == 1)
            {
                break;
            }

            if (logRowQueue.TryDequeue(out var row))
            {
                var (logger, logFileWriter, logRowValues) = row;
                if (logFileWriter != null)
                {
                    logFileWriter.WriteLine(logRowValues);
                }

                yield return new LogRowValues(logRowValues);
            }
        }
    }*/
}
