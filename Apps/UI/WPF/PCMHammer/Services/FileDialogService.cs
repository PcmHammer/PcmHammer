using Microsoft.Win32;

namespace PCMHammer.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string? OpenBinFileDialog()
        {
            OpenFileDialog openFileDialog = new()
            {
                Title = "Select PCM Binary File",
                Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
                DefaultExt = ".bin"
            };

            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;
        }

        public string? SaveBinFileDialog()
        {
            SaveFileDialog saveFileDialog = new()
            {
                Title = "Save PCM Binary Content",
                Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
                DefaultExt = ".bin",
                FileName = "PCM_Read.bin"
            };

            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
        }
    }
}
