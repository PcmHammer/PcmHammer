using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
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
    private ISettingsService settingsService;
    private LoggerAdapter progressLogger;
    private List<RecentFileListItem> recentFiles = new List<RecentFileListItem>();
    public IEnumerable<RecentFileListItem> RecentFiles { get => recentFiles; }

    public DataLoggingModel(
        INavigator navigator,
        IConnectionService vehicleService,
        ISettingsService settingsService,
        LoggerAdapter progressLogger)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.settingsService = settingsService;
        this.progressLogger = progressLogger;

        //this.recentFiles.Add(new RecentFileListItem("One"));
        //this.recentFiles.Add(new RecentFileListItem("Two"));
        //this.recentFiles.Add(new RecentFileListItem("Three"));
        foreach(string path in this.settingsService.GetMruLogProfiles())
        {
            this.recentFiles.Add(new RecentFileListItem(path));
        }
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

        this.settingsService.AddMruLogProfile(path);
        await this.OpenFile(path);
    }

    [Command]
    public Task NewProfileClicked()
    {  
        return this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this);
    }

    [Command]
    public async Task OpenProfileClicked()
    {
        FileOpenPicker openPicker = new FileOpenPicker();
        //openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;        
        openPicker.FileTypeFilter.Add(".logprofile");
        StorageFile file = await openPicker.PickSingleFileAsync();
        if (file == null)
        {
            return;
        }

        string path = file.Path ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        if (!string.IsNullOrEmpty(directory))
        {
            this.settingsService.SetMruLogProfilePath(directory);
        }

        this.settingsService.AddMruLogProfile(path);
        await this.OpenFile(path);
    }

    [Command]
    public Task SaveProfileClicked()
    {
        return Task.CompletedTask;
    }

    [Command]
    public Task SaveProfileAsClicked()
    {
        return Task.CompletedTask;
    }

    private async Task OpenFile(string path)
    {
        await this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this, data: path);
    }
}
