using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace PCMHammerAvalonia.Services;

public interface IFileDialogService
{
    /// <summary>
    /// Opens a directory selection dialog.
    /// </summary>
    /// <param name="initialDirectory">The initial directory to open in the dialog.</param>
    /// <returns>The selected directory path, or null if canceled.</returns>
    Task<string?> OpenDirectoryDialog(string? initialDirectory = null);

    /// <summary>
    /// Prompts the user to select a .bin file.
    /// </summary>
    /// <returns>The full path to the file, or null if canceled.</returns>
    Task<string?> OpenBinFileDialog();

    /// <summary>
    /// Prompts the user to choose a save destination for a .bin file.
    /// </summary>
    /// <returns>The full path to save the file, or null if canceled.</returns>
    Task<string?> SaveBinFileDialog();

    /// <summary>
    /// Prompts the user to choose a save destination for a .bin file and returns the destination
    /// itself. Used on platforms (Android) where the destination is a content:// file with no local
    /// path, so the caller streams bytes into it rather than reading to a path.
    /// </summary>
    /// <param name="suggestedFileName">The default file name to suggest in the picker.</param>
    /// <returns>The chosen destination file, or null if canceled.</returns>
    Task<IStorageFile?> PickBinSaveFileAsync(string suggestedFileName = "PCM_Read");

    /// <summary>
    /// Handles customizable log saving locations
    /// </summary>
    /// <param name="defaultFileName">The default file name to use if the user does not specify one.</param>
    /// <returns>The full path to save the file, or null if canceled.</returns>
    Task<string?> GetLogSavePath(string defaultFileName);
}