//using Android.Text.Style;
//using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record RecentFileListItem(string Path, bool Modified)
{
    public string FileName
    {
        get
        {
            return System.IO.Path.GetFileNameWithoutExtension(this.Path);
        }
    }
    public string DisplayFileName
    {
        get
        {
            string suffix = this.Modified ? " (modified)" : string.Empty;
            return System.IO.Path.GetFileNameWithoutExtension(this.Path) + suffix;
        }
    }

    public string Location
    {
        get
        {
            return System.IO.Path.GetDirectoryName(this.Path) ?? string.Empty;
        }
    }
}

public record ModifiedLogProfile(string Path, LogProfile Profile);

public partial class DataLoggingModel
{
    private const string defaultFileTypeFilter = ".LogProfile";
    public static LoggingContext? ModifiedLoggingContext = null;
    private static string? lastOpenedPath = null;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter progressLogger;
    private readonly ParameterDatabase database;
    private readonly IDispatcher dispatcher;

    public IListState<RecentFileListItem> RecentFiles => ListState<RecentFileListItem>.Empty(this).Selection(RecentFileSelection);

    public IState<RecentFileListItem> RecentFileSelection => State<RecentFileListItem>
        .Empty(this)
        .ForEach(action: this.RecentLogProfileSelectionChanged);

    public IState<bool> SaveButtonEnabled => State<bool>.Value(this, () => ModifiedLoggingContext != null);
    public IState<bool> SaveAsButtonEnabled => State<bool>.Value(this, () => ModifiedLoggingContext != null);


    public DataLoggingModel(
        INavigator navigator,
        IConnectionService vehicleService,
        ISettingsService settingsService,
        IDispatcher dispatcher,
        LoggerAdapter progressLogger)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.settingsService = settingsService;
        this.progressLogger = progressLogger;
        this.dispatcher = dispatcher;

        var _ = this.InitializeMruList();

        // TODO: inject the parameter-database dependency
        string appDirectory = AppContext.BaseDirectory;
        this.database = new ParameterDatabase(appDirectory);
        this.database.LoadDatabase();
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

            bool modified = path == ModifiedLoggingContext?.ProfilePath;
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
        }
    }

    private async ValueTask RecentLogProfileSelectionChanged(RecentFileListItem? item, CancellationToken ct)
    {
        await this.SaveAsButtonEnabled.SetAsync(item != null, ct);
    }

    private async Task PromptToSaveIfModified()
    {
        if (ModifiedLoggingContext == null)
        {
            return;
        }

        var prompt = new ContentDialog
        {
            Title = "Are you sure?",
            Content = $"This log profile has unsaved changes:{Environment.NewLine}{ModifiedLoggingContext.ProfilePath}{Environment.NewLine}Do you want to save it before continuing?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't Save",
        };

        prompt.XamlRoot = XamlRootService.GetXamlRoot();

        try
        {
            if (await prompt.ShowAsync() == ContentDialogResult.Primary)
            {
                await this.SaveProfileAsClicked();
            }
        }
        catch (Exception ex)
        {
            ex.ToString();
        }
    }

    public async Task OpenRecentLogProfile(RecentFileListItem recentFile)
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
    public async ValueTask OpenProfileClicked()
    {
        await this.dispatcher.ExecuteAsync(async () =>
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
        });
    }

    [Command]
    public async Task SaveProfileClicked()
    {
        if (ModifiedLoggingContext == null)
        {
            return;
        }

        // Write() is synchronous, but this function needs to return a Task because it's invoked by the MVUX framework.
        LogProfileWriter.Write(ModifiedLoggingContext.LogProfile, ModifiedLoggingContext.ProfilePath);

        // This is really overkill just to clear the 'modified' flag
        ModifiedLoggingContext = null;
        await this.InitializeMruList();
        await this.SaveButtonEnabled.SetAsync(false);
    }

    private async Task<string?> GetSaveAsPath()
    {
        string? result = null;

        string? currentFileName = (await this.RecentFileSelection.Value())?.DisplayFileName;
        if (currentFileName == null)
        {
            return null;
        }            

        await this.dispatcher.ExecuteAsync(async () =>
        {
            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedFileName = currentFileName;

            // https://platform.uno/docs/articles/features/windows-storage-pickers.html
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.StaticMainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeChoices.Add("Log Profile", new List<string> { defaultFileTypeFilter });
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            StorageFile file = await picker.PickSaveFileAsync();
            result = file?.Path;
        });

        return result;
    }

    private async Task UpdateMruList(string path)
    {
        // Surely there is a cheaper way to clear the 'modified' flag...
        ModifiedLoggingContext = null;
        this.settingsService.AddMruLogProfile(path);
        await this.InitializeMruList();
    }

    [Command]
    public async Task SaveProfileAsClicked()
    {
        LogProfile? profile = ModifiedLoggingContext?.LogProfile;
        if (profile == null)
        {
            // Just copy the selected file to a new name or location.
            await CopyFile();
            return;
        }

        // Save the current profile to a new file.
        string? destinationPath = await this.GetSaveAsPath();
        if (destinationPath == null)
        {
            return;
        }

        LogProfileWriter.Write(profile, destinationPath);

        ModifiedLoggingContext = null;
        await this.UpdateMruList(destinationPath);
        await this.SaveButtonEnabled.SetAsync(false);
    }

    private async Task CopyFile()
    {
        RecentFileListItem? item = await this.RecentFiles.GetSelectedItem();
        if (item == null)
        {
            return;
        }

        string? destinationPath = await this.GetSaveAsPath();
        if (destinationPath == null)
        {
            return;
        }

        try
        {
            // overwrite = true because the user had to agree to overwrite to get here.
            File.Copy(item.Path, destinationPath, true);
            ModifiedLoggingContext = null;
            await this.UpdateMruList(destinationPath);
            await this.SaveButtonEnabled.SetAsync(false);
        }
        catch (Exception exception)
        {
            exception.ToString();
        }
    }

    private async Task OpenFile(string path)
    {
        await this.PromptToSaveIfModified();

        lastOpenedPath = path;
        ModifiedLoggingContext = null;
        LogProfile? profile = null;
        uint operatingSystemId = 0;

        // Load the log profile
        do
        {
            try
            {
                using (var lease = await this.connectionService.BeginActivity("Loading Profile"))
                {

                    try
                    {
                        var osidQueryResult = await lease.Vehicle.QueryOperatingSystemId(CancellationToken.None);
                        operatingSystemId = osidQueryResult.Value;
                    }
                    catch (Exception ex)
                    {
                        this.progressLogger.AddDebugMessage("DataLoggingModel: Unable to query the operating system ID: " + Environment.NewLine + ex.Message);
                        return;
                    }

                    LogProfileReader reader = new LogProfileReader(database, operatingSystemId, this.progressLogger);
                    profile = reader.Read(path);
                    this.progressLogger.AddDebugMessage("DataLoggingParametersModel loaded profile.");
                }

                var profileAndDatabase = new LoggingContext(profile, path, operatingSystemId, this.database);

                await this.navigator.NavigateViewModelAsync<DataLoggingParametersModel>(this, data: profileAndDatabase);
                return;
            }
            catch (ConnectionUnavailableException)
            {
                var yesNo = new ContentDialog();
                yesNo.Title = "Unable to connect.";
                yesNo.Content = "Try again?";
                yesNo.PrimaryButtonText = "Yes";
                yesNo.SecondaryButtonText = "No";
                yesNo.XamlRoot = XamlRootService.GetXamlRoot();
                var result = await yesNo.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    continue;
                }
                else
                {
                    return;
                }
            }
        } while (true);
    }
}
