using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingParametersModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private PcmHacking.ILogger progressLogger;
    private string filePath;

    public DataLoggingParametersModel(
        INavigator navigator,
        IConnectionService vehicleService,
        PcmHacking.ILogger progressLogger,
        string filePath)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.progressLogger = progressLogger;
        this.filePath = filePath;
    }
}
