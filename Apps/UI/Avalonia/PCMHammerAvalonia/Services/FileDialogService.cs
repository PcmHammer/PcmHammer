using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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
        return null;
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
        return files.FirstOrDefault()?.Path.LocalPath;
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