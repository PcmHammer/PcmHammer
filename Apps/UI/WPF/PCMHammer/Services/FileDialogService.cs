using Microsoft.Win32;

namespace PCMHammer.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string OpenBinFileDialog()
        {
            OpenFileDialog openFileDialog = new()
            {
                Title = "Select PCM Binary File",
                Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
                DefaultExt = ".bin"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                return openFileDialog.FileName;
            }

            return null;
        }
    }
}
