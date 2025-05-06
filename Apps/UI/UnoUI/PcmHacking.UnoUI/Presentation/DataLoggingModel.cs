using Microsoft.UI.Xaml;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record RecentFileListItem(string Path, bool Modified)
{
    public override string ToString()
    {
        string suffix = this.Modified ? " (modified)" : string.Empty;
        return System.IO.Path.GetFileNameWithoutExtension(this.Path) + suffix;
    }
}

public record ModifiedLogProfile(string Path, LogProfile Profile);

public partial class DataLoggingModel
{
    private const string defaultFileTypeFilter = ".LogProfile";
    public static ModifiedLogProfile? ModifiedLogProfile = null;
    private static string? lastOpenedPath = null;
    private INavigator navigator;
    private IConnectionService connectionService;
    private ISettingsService settingsService;
    private LoggerAdapter progressLogger;

    public IListState<RecentFileListItem> RecentFiles => ListState<RecentFileListItem>.Empty(this);
    public IState<bool> SaveButtonEnabled => State<bool>.Value(this, () => ModifiedLogProfile != null);
    public IState<bool> SaveAsButtonEnabled => State<bool>.Value(this, () => ModifiedLogProfile != null);


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

        var _ = this.InitializeMruList();
    }

    private async Task InitializeMruList()
    {
        List<RecentFileListItem> recentFiles = new();
        RecentFileListItem? first = null;
        foreach (string path in this.settingsService.GetMruLogProfiles())
        {
            if (path == null)
            {
                continue;
            }

            bool modified = path == ModifiedLogProfile?.Path;
            if (modified)
            {
                await this.SaveButtonEnabled.SetAsync(true);
            }

            var listItem = new RecentFileListItem(path, modified);

            // The first item in the list is the most recent one, so we want to select it.
            if (first == null)
            {
                first = listItem;
            }

            recentFiles.Add(listItem);
        }

        await this.RecentFiles.Update(updater: existing => recentFiles.ToImmutableList(), ct: CancellationToken.None);

        // This seems like an Uno bug - without the delay, the MRU item doesn't get selected.
        await Task.Delay(500);

        if (first != null)
        {
            // This will run asynchronously.
            var _ = await this.RecentFiles.TrySelectAsync(first);
            _.ToString();
        }
    }

    private async Task PromptToSaveIfModified()
    {
        if (ModifiedLogProfile == null)
        {
            return;
        }

        var prompt = new ContentDialog
        {
            Title = "Are you sure?",
            Content = $"This log profile has unsaved changes:{Environment.NewLine}{ModifiedLogProfile.Path}{Environment.NewLine}Do you want to save it before continuing?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't Save",
        };

        prompt.XamlRoot = XamlRootService.GetXamlRoot();

        if (await prompt.ShowAsync() == ContentDialogResult.Primary)
        {
            await this.SaveProfileAsClicked();
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
        await this.PromptToSaveIfModified();
        await this.OpenFile(path);
    }

    [Command]
    public async Task NewProfileClicked()
    {
        await this.PromptToSaveIfModified();
        await this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this);
    }

    [Command]
    public async Task OpenProfileClicked()
    {
        await this.PromptToSaveIfModified();

        FileOpenPicker picker = new FileOpenPicker();

        // https://platform.uno/docs/articles/features/windows-storage-pickers.html
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StaticMainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;        
        picker.FileTypeFilter.Add(defaultFileTypeFilter);
        StorageFile file = await picker.PickSingleFileAsync();
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
    public async Task SaveProfileClicked()
    {
        if (ModifiedLogProfile == null)
        {
            return;
        }

        // Write() is synchronous, but this function needs to return a Task because it's invoked by the MVUX framework.
        LogProfileWriter.Write(ModifiedLogProfile.Profile, ModifiedLogProfile.Path);

        // This is really overkill just to clear the 'modified' flag
        ModifiedLogProfile = null;
        await this.InitializeMruList();
        await this.SaveButtonEnabled.SetAsync(false);
    }

    private async Task<string?> GetSaveAsPath()
    {
        FileSavePicker picker = new FileSavePicker();

        // https://platform.uno/docs/articles/features/windows-storage-pickers.html
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StaticMainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeChoices.Add("Log Profile", new List<string> { defaultFileTypeFilter });
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        StorageFile file = await picker.PickSaveFileAsync();
        if (file == null)
        {
            return null;
        }

        return file.Path;
    }

    private async Task UpdateMruList(string path)
    {
        // Surely there is a cheaper way to clear the 'modified' flag...
        ModifiedLogProfile = null;
        this.settingsService.AddMruLogProfile(path);
        await this.InitializeMruList();
    }

    [Command]
    public async Task SaveProfileAsClicked()
    {
        LogProfile? profile = ModifiedLogProfile?.Profile;
        string? destinationPath = null;
        if (profile == null)
        {
            // Don't open the profile, just copy it.
            RecentFileListItem? item = await this.RecentFiles.GetSelectedItem();
            if (item == null)
            {
                return;
            }

            destinationPath = await this.GetSaveAsPath();
            if (destinationPath == null)
            {
                return;
            }

            try
            {
                // overwrite = true because the user had to agree to overwrite to get here.
                File.Copy(item.Path, destinationPath, true);
                ModifiedLogProfile = null;
                await this.UpdateMruList(destinationPath);
                await this.SaveButtonEnabled.SetAsync(false);
            }
            catch (Exception exception)
            {
                exception.ToString();
            }
            return;
        }

        destinationPath = await this.GetSaveAsPath();
        if (destinationPath == null)
        {
            return;
        }

        LogProfileWriter.Write(profile, destinationPath);

        ModifiedLogProfile = null;
        await this.UpdateMruList(destinationPath);
        await this.SaveButtonEnabled.SetAsync(false);
    }

    private async Task OpenFile(string path)
    {
        await this.PromptToSaveIfModified();

        lastOpenedPath = path;
        ModifiedLogProfile = null;
        await this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this, data: path);
    }
}
