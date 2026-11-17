using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMHammerAvalonia.Services;

namespace PCMHammerAvalonia.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        // Private fields
        private readonly FileDialogService _fileDialogService;
        private readonly SettingsService _settingsService;

        #region Properties
        [ObservableProperty]
        public partial string BinDirectory { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string LogDirectory { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool RetainDeviceConfigurationOnExit { get; set; }

        [ObservableProperty]
        public partial bool UseLogSaveAsDialog { get; set; }

        [ObservableProperty]
        public partial bool SaveResultsLogOnExit { get; set; }

        [ObservableProperty]
        public partial bool SaveDebugLogOnExit { get; set; }
        #endregion

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        #region Commands
        [RelayCommand]
        public void SelectBinDirectory()
        {
            string? selectedDirectory = _fileDialogService.OpenDirectoryDialog(BinDirectory).Result;

            if (string.IsNullOrWhiteSpace(selectedDirectory))
                return;

            BinDirectory = selectedDirectory;
        }

        [RelayCommand]
        public void SelectLogDirectory()
        {
            string? selectedDirectory = _fileDialogService.OpenDirectoryDialog(LogDirectory).Result;

            if (string.IsNullOrWhiteSpace(selectedDirectory))
                return;

            LogDirectory = selectedDirectory;
        }

        [RelayCommand]
        public void Close() => RequestClose?.Invoke();

        [RelayCommand]
        public void Accept()
        {
            SaveSettings();
            RequestAcceptandClose?.Invoke();
        }
        #endregion


        public SettingsViewModel(FileDialogService fileDialogService, SettingsService settingsService)
        {
            _fileDialogService = fileDialogService;
            _settingsService = settingsService;
            InitializeSettings();
        }

        public void InitializeSettings()
        {
            BinDirectory = _settingsService.Settings.BinDirectory;
            LogDirectory = _settingsService.Settings.LogDirectory;
            RetainDeviceConfigurationOnExit = _settingsService.Settings.RetainDeviceConfigurationOnExit;
            UseLogSaveAsDialog = _settingsService.Settings.UseLogSaveAsDialog;
            SaveResultsLogOnExit = _settingsService.Settings.SaveResultsLogOnExit;
            SaveDebugLogOnExit = _settingsService.Settings.SaveDebugLogOnExit;
        }

        public void SaveSettings()
        {
            _settingsService.Settings.BinDirectory = BinDirectory; 
            _settingsService.Settings.LogDirectory = LogDirectory;
            _settingsService.Settings.RetainDeviceConfigurationOnExit = RetainDeviceConfigurationOnExit;
            _settingsService.Settings.UseLogSaveAsDialog = UseLogSaveAsDialog;
            _settingsService.Settings.SaveResultsLogOnExit = SaveResultsLogOnExit;
            _settingsService.Settings.SaveDebugLogOnExit = SaveDebugLogOnExit;
            _settingsService.SaveSettings();
        }
    }
}
