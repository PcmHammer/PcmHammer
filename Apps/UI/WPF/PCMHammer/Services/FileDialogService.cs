using Microsoft.Win32;

namespace PCMHammer.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string? OpenDirectoryDialog(string? initialDirectory = null)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Bin Directory",
                InitialDirectory = !string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory)
                    ? initialDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FolderName : null;
        }

        public string? OpenBinFileDialog()
        {
            OpenFileDialog openFileDialog = new()
            {
                Title = "Select PCM Binary File",
                Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
                DefaultExt = ".bin"
            };

            if (Properties.Settings.Default.BinDirectory != null)
                openFileDialog.InitialDirectory = Properties.Settings.Default.BinDirectory;

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

        public string GetLogSavePath(string defaultFileName)
        {
            // Interactive Dialog Prompt Branch
            SaveFileDialog saveFileDialog = new()
            {
                Title = "Save Log Contents",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1,
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                FileName = defaultFileName,
                DefaultExt = ".txt"
            };

            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : string.Empty;
        }
    }
}
