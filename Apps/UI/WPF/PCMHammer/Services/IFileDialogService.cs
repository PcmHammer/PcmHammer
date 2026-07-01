namespace PCMHammer.Services
{
    public interface IFileDialogService
    {
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
        /// Handles customizable log saving locations
        /// </summary>
        /// <param name="defaultFileName">The default file name to use if the user does not specify one.</param>
        /// <returns>The full path to save the file, or null if canceled.</returns>
        string GetLogSavePath(string defaultFileName);
    }
}
