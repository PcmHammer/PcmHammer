namespace PCMHammer.Services
{
    public interface IFileDialogService
    {
        /// <summary>
        /// Opens a directory selection dialog.
        /// </summary>
        /// <param name="initialDirectory">The initial directory to open in the dialog.</param>
        /// <returns>The selected directory path, or null if canceled.</returns>
        string? OpenDirectoryDialog(string? initialDirectory = null);

        /// <summary>
        /// Prompts the user to select a .bin file.
        /// </summary>
        /// <returns>The full path to the file, or null if canceled.</returns>
        string? OpenBinFileDialog();

        /// <summary>
        /// Prompts the user to choose a save destination for a .bin file.
        /// </summary>
        /// <returns>The full path to save the file, or null if canceled.</returns>
        string? SaveBinFileDialog();

        /// <summary>
        /// Prompts the user to open a PcmHammer document (a .phz package or a raw .bin) to load into the
        /// working document.
        /// </summary>
        /// <returns>The full path to the file, or null if canceled.</returns>
        string? OpenPackageFileDialog();

        /// <summary>
        /// Prompts for a save destination for the working document. A complete package may be saved as
        /// .phz (default) or .bin; an incomplete one may only be saved as .bin.
        /// </summary>
        /// <param name="complete">Whether the document is a complete package (offers .phz).</param>
        /// <param name="suggestedName">A default file name (without extension) to pre-fill.</param>
        /// <returns>The chosen path, or null if canceled.</returns>
        string? SavePackageFileDialog(bool complete, string suggestedName);

        /// <summary>
        /// Prompts for the base path used when exporting one or more images as raw .bin files. The caller
        /// derives per-image file names from this base.
        /// </summary>
        /// <param name="suggestedName">A default base file name (without extension) to pre-fill.</param>
        /// <returns>The chosen base path, or null if canceled.</returns>
        string? ExportBinBaseDialog(string suggestedName);

        /// <summary>
        /// Handles customizable log saving locations
        /// </summary>
        /// <param name="defaultFileName">The default file name to use if the user does not specify one.</param>
        /// <returns>The full path to save the file, or null if canceled.</returns>
        string GetLogSavePath(string defaultFileName);
    }
}
