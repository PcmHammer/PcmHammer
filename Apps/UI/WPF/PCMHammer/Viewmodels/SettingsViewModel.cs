using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.Services;

namespace PCMHammer.Viewmodels
{
    public partial class SettingsViewModel : ObservableObject
    {
        // Private fields
        private readonly FileDialogService _fileDialogService;

        #region Properties
        [ObservableProperty]
        public partial string BinDirectory { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string LogDirectory { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool AutoInitializeLastDevice { get; set; }

        [ObservableProperty]
        public partial bool UseLogSaveAsDialog { get; set; }

        [ObservableProperty]
        public partial bool SaveResultsLogOnExit { get; set; }

        [ObservableProperty]
        public partial bool SaveDebugLogOnExit { get; set; }

        // Advanced, per-session (RuntimeSettings) flags. These intentionally do not persist across
        // launches - they reset to their defaults every time the app starts.
        [ObservableProperty]
        public partial bool AllowCrossFlashing { get; set; }

        [ObservableProperty]
        public partial bool ForceWriteAllSectors { get; set; }

        [ObservableProperty]
        public partial bool AllowModuleImport { get; set; }
        #endregion

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        #region Commands
        [RelayCommand]
        public void SelectBinDirectory()
        {
            string? selectedDirectory = _fileDialogService.OpenDirectoryDialog(BinDirectory);

            if (string.IsNullOrWhiteSpace(selectedDirectory))
                return;

            BinDirectory = selectedDirectory;
        }

        [RelayCommand]
        public void SelectLogDirectory()
        {
            string? selectedDirectory = _fileDialogService.OpenDirectoryDialog(LogDirectory);

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

        public SettingsViewModel(FileDialogService fileDialogService)
        {
            _fileDialogService = fileDialogService;
            InitializeSettings();
        }

        public void InitializeSettings()
        {
            BinDirectory = Properties.Settings.Default.BinDirectory;
            LogDirectory = Properties.Settings.Default.LogDirectory;
            AutoInitializeLastDevice = Properties.Settings.Default.RetainDeviceConfigurationOnExit;
            UseLogSaveAsDialog = Properties.Settings.Default.UseLogSaveAsDialog;
            SaveResultsLogOnExit = Properties.Settings.Default.SaveResultsLogOnExit;
            SaveDebugLogOnExit = Properties.Settings.Default.SaveDebugLogOnExit;

            // Per-session flags come from RuntimeSettings, not the persisted settings file.
            AllowCrossFlashing = RuntimeSettings.AllowCrossFlashing;
            ForceWriteAllSectors = RuntimeSettings.ForceWriteAllSectors;
            AllowModuleImport = RuntimeSettings.AllowModuleImport;
        }

        public void SaveSettings()
        {
            Properties.Settings.Default.BinDirectory = BinDirectory;
            Properties.Settings.Default.LogDirectory = LogDirectory;
            Properties.Settings.Default.RetainDeviceConfigurationOnExit = AutoInitializeLastDevice;
            Properties.Settings.Default.UseLogSaveAsDialog = UseLogSaveAsDialog;
            Properties.Settings.Default.SaveResultsLogOnExit = SaveResultsLogOnExit;
            Properties.Settings.Default.SaveDebugLogOnExit = SaveDebugLogOnExit;
            Properties.Settings.Default.Save();

            // Per-session flags: applied to RuntimeSettings, never written to disk.
            RuntimeSettings.AllowCrossFlashing = AllowCrossFlashing;
            RuntimeSettings.ForceWriteAllSectors = ForceWriteAllSectors;
            RuntimeSettings.AllowModuleImport = AllowModuleImport;
        }
    }
}
