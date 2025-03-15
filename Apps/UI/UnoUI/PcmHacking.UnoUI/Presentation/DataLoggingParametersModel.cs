using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingParametersModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private ISettingsService settingsService;
    private PcmHacking.ILogger progressLogger;
    private string profilePath;
    //private string canPortName;
    private CanLogger? canLogger;

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
        //this.canPortName = settingsService.GetCanSerialPortName(CancellationToken.None).Result;

        dispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(100);
            await this.OpenProfile();
        });
    }

    private async Task OpenProfile()
    {
        var lease = await this.connectionService.BeginActivity("Logging", true);
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
        this.populateParameterGrid(profile);

        // Create the logger
        this.canLogger = new CanLogger(database);

        /*
        if (string.IsNullOrEmpty(this.canPortName))
        {
            await this.canLogger.SetPort(null);
        }
        else
        {
            await this.canLogger.SetPort(new StandardPort(canPortName));

        }

        Logger logger = this.Vehicle.CreateLogger(this.osid, canLogger, this.currentProfile.Columns, this);
        */

    }

    private void populateParameterGrid(LogProfile profile)
    {
        //foreach (LogColumn column in this.profile.Columns)
        {
            //       this.Param
        }
    }
}
