using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using Uno.Extensions;

namespace PcmHacking.UnoUI.Presentation;

public record LogRowValues(IEnumerable<string> Values);

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
    private EventWaitHandle rowAvailableHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

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

        dispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(100);
            await this.OpenProfile();
        });
    }

    public IState<LogProfile> LogProfile => State<LogProfile>.Empty(this);
    public IFeed<LogRowValues> Rows => Feed<LogRowValues>.AsyncEnumerable(this.RowFactory);

    private async Task OpenProfile()
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
            var osidQueryResult = await vehicle.QueryOperatingSystemId(CancellationToken.None);
            uint osid = osidQueryResult.Value;
            LogProfileReader reader = new LogProfileReader(database, osid, this.progressLogger);
            var profile = reader.Read(this.profilePath);

            // This tells the view to update the UI with the new profile.
            await this.LogProfile.UpdateAsync(currentValue => profile, CancellationToken.None);

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
            while (!exitWaitHandle.WaitOne(0))
            {
                IEnumerable<string> rowValues = await logger.GetNextRow();
                if (rowValues != null)
                {
                    // Hand this data off to be written to disk and displayed in the UI.
                    this.logRowQueue.Enqueue(
                        new Tuple<Logger, LogFileWriter?, IEnumerable<string>>(
                            logger,
                            null, // file writer
                            rowValues));

                    this.rowAvailableHandle.Set();
                }
            }
        }
    }

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
    }
}
