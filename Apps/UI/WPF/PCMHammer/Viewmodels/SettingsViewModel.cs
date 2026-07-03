using PCMHammer.Helpers;
using PCMHammer.Services;
using PCMHammer.ViewModels;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        // Private fields
        private readonly FileDialogService _fileDialogService;

        // Properties
        private string _binDirectory;
        public string BinDirectory
        {
            get => _binDirectory; 
            set => SetProperty(ref _binDirectory, value);
        }
        private string _logDirectory;
        public string LogDirectory
        {
            get => _logDirectory;
            set => SetProperty(ref _logDirectory, value);
        }
        private bool _retainDeviceConfigurationOnExit;
        public bool RetainDeviceConfigurationOnExit
        {
            get => _retainDeviceConfigurationOnExit;
            set => SetProperty(ref _retainDeviceConfigurationOnExit, value);
        }
        private bool _useLogSaveAsDialog;
        public bool UseLogSaveAsDialog
        {
            get => _useLogSaveAsDialog;
            set => SetProperty(ref _useLogSaveAsDialog, value);
        }
        private bool _saveResultsLogOnExit;
        public bool SaveResultsLogOnExit
        {
            get => _saveResultsLogOnExit;
            set => SetProperty(ref _saveResultsLogOnExit, value);
        }
        private bool _saveDebugLogOnExit;
        public bool SaveDebugLogOnExit
        {
            get => _saveDebugLogOnExit;
            set => SetProperty(ref _saveDebugLogOnExit, value);
        }

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        // Commands
        public ICommand SelectBinDirectoryCommand { get; }
        public ICommand SelectLogDirectoryCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand AcceptCommand { get; }

        public SettingsViewModel(FileDialogService fileDialogService)
        {
            _fileDialogService = fileDialogService;
            _binDirectory = Properties.Settings.Default.BinDirectory;
            _retainDeviceConfigurationOnExit = Properties.Settings.Default.RetainDeviceConfigurationOnExit;
            _useLogSaveAsDialog = Properties.Settings.Default.UseLogSaveAsDialog;
            _saveResultsLogOnExit = Properties.Settings.Default.SaveResultsLogOnExit;
            _saveDebugLogOnExit = Properties.Settings.Default.SaveDebugLogOnExit;
            _logDirectory = Properties.Settings.Default.LogDirectory;
            SelectBinDirectoryCommand = new RelayCommand(SelectBinDirectory);
            SelectLogDirectoryCommand = new RelayCommand(SelectLogDirectory);
            CloseCommand = new RelayCommand(() => { RequestClose?.Invoke(); });
            AcceptCommand = new RelayCommand(() =>
            {
                SaveSettings();
                RequestAcceptandClose?.Invoke();
            });
        }

        public void SelectBinDirectory()
        {
            string? selectedDirectory = _fileDialogService.OpenDirectoryDialog(BinDirectory);

            if (string.IsNullOrWhiteSpace(selectedDirectory))
                return;

            BinDirectory = selectedDirectory;
        }

        public void SelectLogDirectory()
        {
            string? selectedDirectory = _fileDialogService.OpenDirectoryDialog(LogDirectory);

            if (string.IsNullOrWhiteSpace(selectedDirectory))
                return;

            LogDirectory = selectedDirectory;
        }

        public void SaveSettings()
        {
            Properties.Settings.Default.BinDirectory = BinDirectory; 
            Properties.Settings.Default.LogDirectory = LogDirectory;
            Properties.Settings.Default.RetainDeviceConfigurationOnExit = RetainDeviceConfigurationOnExit;
            Properties.Settings.Default.UseLogSaveAsDialog = UseLogSaveAsDialog;
            Properties.Settings.Default.SaveResultsLogOnExit = SaveResultsLogOnExit;
            Properties.Settings.Default.SaveDebugLogOnExit = SaveDebugLogOnExit;
            Properties.Settings.Default.Save();
        }
    }
}
