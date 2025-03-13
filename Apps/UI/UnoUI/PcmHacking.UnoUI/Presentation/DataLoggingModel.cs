using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record RecentFileListItem(string Path)
{
    public override string ToString()
    {
        return System.IO.Path.GetFileNameWithoutExtension(this.Path);
    }
}

public partial class DataLoggingModel
{
    private INavigator navigator;
    private IConnectionService connectionService;
    private PcmHacking.ILogger progressLogger;
    private List<RecentFileListItem> recentFiles = new List<RecentFileListItem>();
    public IEnumerable<RecentFileListItem> RecentFiles { get => recentFiles; }

    public DataLoggingModel(
        INavigator navigator,
        IConnectionService vehicleService,
        PcmHacking.ILogger progressLogger)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.progressLogger = progressLogger;

        this.recentFiles.Add(new RecentFileListItem("One"));
        this.recentFiles.Add(new RecentFileListItem("Two"));
        this.recentFiles.Add(new RecentFileListItem("Three"));
    }

    public async Task RecentProfileClicked(RecentFileListItem recentFile)
    {
        string path = recentFile.Path;
        if (path == null)
        {
            return;
        }

        if (!System.IO.File.Exists(path))
        {
            // TODO: remove item from recent list
            return;
        }

        await this.OpenFile(recentFile.ToString());
    }

    [Command]
    public void NewProfileClicked(object sender, EventArgs e)
    {        
    }

    [Command]
    public async void OpenProfileClicked(object sender, EventArgs e)
    {
        FileOpenPicker openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".bin");
        StorageFile file = await openPicker.PickSingleFileAsync();
        if (file == null)
        {
            return;
        }

        
        await this.OpenFile(file.Path);
    }

    [Command]
    public void SaveProfileClicked(object sender, EventArgs e)
    {

    }

    [Command]
    public void SaveProfileAsClicked(object sender, EventArgs e)
    {

    }

    private async Task OpenFile(string path)
    {
        await this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this, data: path);
    }
}
