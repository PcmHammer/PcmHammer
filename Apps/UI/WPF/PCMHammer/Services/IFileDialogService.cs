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
    }
}
