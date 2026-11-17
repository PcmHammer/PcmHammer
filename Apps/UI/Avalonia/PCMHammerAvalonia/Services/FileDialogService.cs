using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PCMHammerAvalonia.Views;
// On the android target Android.App.Application is also in scope; pin Application to Avalonia's.
using Application = Avalonia.Application;

namespace PCMHammerAvalonia.Services;

public class FileDialogService(SettingsService settingsService) : IFileDialogService
{
    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.StorageProvider;
        }

        // On single-view platforms (Android) there is no desktop main window; reach the storage
        // provider through the live MainView's TopLevel instead.
        return TopLevel.GetTopLevel(MainView.Current)?.StorageProvider;
    }

    public async Task<string?> OpenDirectoryDialog(string? initialDirectory = null)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null) return null;

        string startPath = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        IStorageFolder? suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(startPath);

        var options = new FolderPickerOpenOptions
        {
            Title = "Select Bin Directory",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation
        };

        IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(options);
        return folders[0].Path.LocalPath;
    }

    public async Task<string?> OpenBinFileDialog()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null) return null;

        IStorageFolder? suggestedStartLocation = null;
        string? savedDir = settingsService.Settings.BinDirectory;

        if (!string.IsNullOrWhiteSpace(savedDir) && Directory.Exists(savedDir))
        {
            suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(savedDir);
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Select PCM Binary File",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Binary Files (*.bin)") { Patterns = ["*.bin"] },
                FilePickerFileTypes.All
            }
        };

        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(options);
        IStorageFile? picked = files.FirstOrDefault();
        if (picked == null) return null;

#if ANDROID
        // The picked file is a content:// document with no usable local filesystem path, so copy it
        // into a private temp .bin the flasher can read by path. (The temp lives in the app cache; the
        // OS reclaims it.)
        string tempPath = Path.Combine(Path.GetTempPath(), $"pcm_src_{Guid.NewGuid():N}.bin");
        await using (Stream src = await picked.OpenReadAsync())
        await using (Stream dst = File.Create(tempPath))
        {
            await src.CopyToAsync(dst);
        }
        return tempPath;
#else
        return picked.Path.LocalPath;
#endif
    }

    public async Task<string?> SaveBinFileDialog()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = "Save PCM Binary Content",
            DefaultExtension = "bin",
            SuggestedFileName = "PCM_Read.bin",
            FileTypeChoices = [
                new FilePickerFileType("Binary Files (*.bin)") { Patterns = ["*.bin"] },
                FilePickerFileTypes.All
            ]
        };

        IStorageFile? file = await storageProvider.SaveFilePickerAsync(options);

        // Safely retrieve local OS path without throwing NRE or UriFormatException
        return file?.TryGetLocalPath();
    }

    public async Task<IStorageFile?> PickBinSaveFileAsync(string suggestedFileName = "PCM_Read")
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = "Save PCM Binary Content",
            DefaultExtension = "bin",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [
                new FilePickerFileType("Binary Files (*.bin)") { Patterns = ["*.bin"] },
                FilePickerFileTypes.All
            ]
        };

        // Returns the picked destination itself. On Android the destination is a content:// file that
        // has no local filesystem path, so the caller writes bytes into its stream rather than passing
        // a path to the reader.
        return await storageProvider.SaveFilePickerAsync(options);
    }

    public async Task<string?> GetLogSavePath(string defaultFileName)
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null) return null;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        IStorageFolder? startLocation = await storageProvider.TryGetFolderFromPathAsync(baseDir);

        var options = new FilePickerSaveOptions
        {
            Title = "Save Log Contents",
            DefaultExtension = "txt",
            SuggestedFileName = defaultFileName,
            SuggestedStartLocation = startLocation,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text files (*.txt)") { Patterns = ["*.txt"] },
                FilePickerFileTypes.All
            }
        };

        IStorageFile? file = await storageProvider.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
    }
}