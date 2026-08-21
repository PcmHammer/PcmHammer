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

        public string? OpenPackageFileDialog()
        {
            OpenFileDialog openFileDialog = new()
            {
                Title = "Load PCMHammer File",
                Filter = "PcmHammer files (*.phz;*.bin)|*.phz;*.bin|PcmHammer (*.phz)|*.phz|Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
                DefaultExt = ".phz"
            };

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.BinDirectory))
                openFileDialog.InitialDirectory = Properties.Settings.Default.BinDirectory;

            return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;
        }

        public string? SavePackageFileDialog(bool complete, string suggestedName)
        {
            // A complete package can be saved as a self-contained .phz (default) or a raw master .bin; an
            // incomplete document (e.g. a master-only bin for a slave-bearing PCM) offers only .bin so an
            // invalid package can never be produced.
            SaveFileDialog saveFileDialog = new()
            {
                Title = "Save PCMHamer File",
                Filter = complete
                    ? "PcmHammer (*.phz)|*.phz|Binary Files (*.bin)|*.bin"
                    : "Binary Files (*.bin)|*.bin",
                DefaultExt = complete ? ".phz" : ".bin",
                FileName = string.IsNullOrWhiteSpace(suggestedName) ? "PCM_Read" : suggestedName,
                OverwritePrompt = true
            };

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.BinDirectory))
                saveFileDialog.InitialDirectory = Properties.Settings.Default.BinDirectory;

            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
        }

        public string? ExportBinBaseDialog(string suggestedName)
        {
            SaveFileDialog saveFileDialog = new()
            {
                Title = "Export Bin",
                Filter = "Binary Files (*.bin)|*.bin",
                DefaultExt = ".bin",
                FileName = string.IsNullOrWhiteSpace(suggestedName) ? "export" : suggestedName,
                OverwritePrompt = false
            };

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.BinDirectory))
                saveFileDialog.InitialDirectory = Properties.Settings.Default.BinDirectory;

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
