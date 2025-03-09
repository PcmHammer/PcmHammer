using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public record RecentFileListItem(string Text)
{
    public override string ToString()
    {
        return this.Text;
    }
}

public partial class DataLoggingModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private PcmHacking.ILogger logger;
    private List<RecentFileListItem> recentFiles = new List<RecentFileListItem>();
    public IEnumerable<RecentFileListItem> RecentFiles { get => recentFiles; }

    public DataLoggingModel(
        INavigator navigator,
        IConnectionService vehicleService,
        PcmHacking.ILogger progressLogger)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.logger = progressLogger;

        this.recentFiles.Add(new RecentFileListItem("One"));
        this.recentFiles.Add(new RecentFileListItem("Two"));
        this.recentFiles.Add(new RecentFileListItem("Three"));
    }

    [Command]
    public async void RecentFileClicked(object sender, EventArgs e)
    {
        if (this.navigator != null)
        {
            await this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this);
        }
    }
}
