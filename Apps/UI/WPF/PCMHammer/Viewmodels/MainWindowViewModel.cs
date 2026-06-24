using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.Views;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly ILogger _logger;
        public Device? SelectedDevice
        {
            get => _selectedDevice;
            set { _selectedDevice = value; OnPropertyChanged(); }
        }
        private Device? _selectedDevice;

        // --- Status and Progress Properties ---
        private string _logText = string.Empty;
        public string LogText
        {
            get => _logText;
            set { _logText = value; OnPropertyChanged(); }
        }
        private string _debugLogText = string.Empty;
        public string DebugLogText
        {
            get => _debugLogText;
            set { _debugLogText = value; OnPropertyChanged(); }
        }
        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private int _retryCount = 0;
        public int RetryCount
        {
            get => _retryCount;
            set { _retryCount = value; OnPropertyChanged(); }
        }

        private double _transferRate = 0.0;
        public double TransferRate
        {
            get => _transferRate;
            set { _transferRate = value; OnPropertyChanged(); }
        }

        private double _progressPercent = 0.0;
        public double ProgressPercent
        {
            get => _progressPercent;
            set { _progressPercent = value; OnPropertyChanged(); }
        }

        private string _timeRemaining = "00:00 Remaining";
        public string TimeRemaining
        {
            get => _timeRemaining;
            set { _timeRemaining = value; OnPropertyChanged(); }
        }
        private Vehicle? _vehicle;
        public Vehicle? Vehicle
        {
            get => _vehicle;
            set
            {
                _vehicle = value;
                OnPropertyChanged();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        // --- Commands (File Menu) ---
        public ICommand SaveResultsLogCommand { get; }
        public ICommand SaveDebugLogCommand { get; }
        public ICommand ExitCommand { get; }

        // --- Commands (Tools Menu & Operations) ---
        public ICommand ReadPCMCommand { get; }
        public ICommand VerifyPCMCommand { get; }
        public ICommand ChangeVINCommand { get; }
        public ICommand WriteParametersCommand { get; }
        public ICommand WriteOSCalibrationBootCommand { get; }
        public ICommand WriteFullFlashCloneCommand { get; }
        public ICommand TestFileChecksumsCommand { get; }
        public ICommand BruteForceUnlockCommand { get; }
        public ICommand HaltRunningKernelCommand { get; }

        // --- Commands (Options Menu) ---
        public ICommand UserDefinedKeyCommand { get; }
        public ICommand SettingsCommand { get; }

        // --- Commands (Device & Operations Sidebar) ---
        public ICommand SelectDeviceCommand { get; }
        public ICommand ReInitializeDeviceCommand { get; }
        public ICommand ReadPropertiesCommand { get; }
        public ICommand WritePCMCommand { get; }
        public ICommand TestWriteCommand { get; }
        public ICommand CancelCurrentCommand { get; }

        public MainWindowViewModel()
        {
            _logger = new MainWindowLogger(this);
            _logger.AddUserMessage("PCM Hammer");
            _logger.AddUserMessage("Copyright (C) 2018-2026 PcmHacking.net - GPL v3");
            _logger.AddUserMessage("Version: 2.0.0");
            _logger.AddUserMessage($"Running at: {DateTime.Now:dddd, MMMM d yyyy, HH:mm:ss}");
            _logger.AddUserMessage("Thanks for using PCM Hammer.");
            _logger.AddDebugMessage("PCM Hammer");
            _logger.AddDebugMessage("Copyright (C) 2018-2026 PcmHacking.net - GPL v3");
            _logger.AddDebugMessage("Version: 2.0.0");
            _logger.AddDebugMessage($"Running at: {DateTime.Now:dddd, MMMM d yyyy, HH:mm:ss}");
            _logger.AddDebugMessage("Thanks for using PCM Hammer.");

            // Initialize Commands with actions
            SaveResultsLogCommand = new RelayCommand(ExecuteSaveResultsLog);
            SaveDebugLogCommand = new RelayCommand(ExecuteSaveDebugLog);
            ExitCommand = new RelayCommand(ExecuteExit);

            ReadPCMCommand = new RelayCommand(ExecuteReadPCM);
            VerifyPCMCommand = new RelayCommand(ExecuteVerifyPCM);
            ChangeVINCommand = new RelayCommand(
                execute: async () => await ExecuteChangeVINAsync()
            );
            WriteParametersCommand = new RelayCommand(ExecuteWriteParameters);
            WriteOSCalibrationBootCommand = new RelayCommand(ExecuteWriteOSCalibrationBoot);
            WriteFullFlashCloneCommand = new RelayCommand(ExecuteWriteFullFlashClone);
            TestFileChecksumsCommand = new RelayCommand(ExecuteTestFileChecksums);
            BruteForceUnlockCommand = new RelayCommand(ExecuteBruteForceUnlock);
            HaltRunningKernelCommand = new RelayCommand(ExecuteHaltRunningKernel);

            UserDefinedKeyCommand = new RelayCommand(ExecuteUserDefinedKey);
            SettingsCommand = new RelayCommand(ExecuteSettings);

            SelectDeviceCommand = new RelayCommand(ExecuteSelectDevice);
            ReInitializeDeviceCommand = new RelayCommand(ExecuteReInitializeDevice, CanReInitialize);
            ReadPropertiesCommand = new RelayCommand(ExecuteReadProperties);
            WritePCMCommand = new RelayCommand(ExecuteWritePCM);
            TestWriteCommand = new RelayCommand(ExecuteTestWrite);
            CancelCurrentCommand = new RelayCommand(ExecuteCancelCurrent);
        }

        // --- Command Execution Methods ---
        private void ExecuteSaveResultsLog() => MessageBox.Show("Saving Results Log...");
        private void ExecuteSaveDebugLog() => MessageBox.Show("Saving Debug Log...");
        private void ExecuteExit() => Application.Current.Shutdown();

        private void ExecuteReadPCM() => StatusText = "Reading PCM...";
        private void ExecuteVerifyPCM() => StatusText = "Verifying PCM...";
        private async Task ExecuteChangeVINAsync()
        {
            try
            {
                Response<uint> osidResponse = await Vehicle!.QueryOperatingSystemId(CancellationToken.None);
                if (osidResponse.Status != ResponseStatus.Success)
                {
                    _logger.AddUserMessage($"Operating system query failed: {osidResponse.Status}");
                    return;
                }

                OSIDInfo info = new(osidResponse.Value);

                var vinResponse = await Vehicle.QueryVin();
                if (vinResponse.Status != ResponseStatus.Success)
                {
                    _logger.AddUserMessage($"VIN query failed: {vinResponse.Status}");
                    return;
                }

                var vinViewModel = new ChangeVINViewModel(vinResponse.Value);
                var vinDialog = new ChangeVINWindow(vinViewModel) { Owner = Application.Current.MainWindow };

                if (vinDialog.ShowDialog() == true)
                {
                    string cleanVin = vinViewModel.Vin.Trim();
                    _logger.AddUserMessage($"Attempting to write updated VIN: {cleanVin}");
                    bool unlocked = await Vehicle.UnlockEcu(info.KeyAlgorithm);
                    if (!unlocked)
                    {
                        _logger.AddUserMessage("Unable to unlock PCM. Authorization Denied.");
                        MessageBox.Show("Unable to unlock PCM. Operation aborted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    Response<bool> vinModified = await Vehicle.UpdateVin(cleanVin);
                    if (vinModified.Value)
                    {
                        _logger.AddUserMessage($"VIN successfully updated to: {cleanVin}");
                        MessageBox.Show($"VIN updated to {cleanVin} successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _logger.AddUserMessage($"Failed to commit changes. Error code: {vinModified.Status}");
                        MessageBox.Show($"Unable to change the VIN. Error: {vinModified.Status}", "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.AddUserMessage($"VIN change failed with exception: {exception.Message}");
            }
        }
        private void ExecuteWriteParameters() => StatusText = "Writing Parameters...";
        private void ExecuteWriteOSCalibrationBoot() => StatusText = "Writing OS/Cal/Boot...";
        private void ExecuteWriteFullFlashClone() => StatusText = "Cloning Full Flash...";
        private void ExecuteTestFileChecksums() => MessageBox.Show("Testing Checksums...");
        private void ExecuteBruteForceUnlock() => StatusText = "Attempting Brute Force Unlock...";
        private void ExecuteHaltRunningKernel() => StatusText = "Kernel Halted.";

        private void ExecuteUserDefinedKey() => MessageBox.Show("Opening Key Configuration...");
        private void ExecuteSettings() => MessageBox.Show("Opening Settings...");

        private void ExecuteSelectDevice()
        {
            Window parentWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
            var pickerDialog = new DevicePicker(_logger) { Owner = parentWindow };

            StatusText = "Selecting Device...";

            // ShowDialog blocks here until RequestAcceptAndClose or RequestClose fires
            if (pickerDialog.ShowDialog() == true)
            {
                // Grab the object directly from the window's property
                Device? workingDevice = (pickerDialog.DataContext as DevicePickerViewModel)?.SelectedDevice;

                if (workingDevice != null)
                {
                    SelectedDevice = workingDevice;
                    Protocol protocolEngine = new();
                    ToolPresentNotifier notifier = new(
                        workingDevice,
                        protocolEngine,
                        _logger
                    );
                    Vehicle = new Vehicle(
                        workingDevice,
                        protocolEngine,
                        _logger,
                        notifier,
                        "PCMHammer"
                    );
                    _logger.AddDebugMessage($"Connected to device: {workingDevice.GetDeviceType()}");
                }
                else
                    _logger.AddDebugMessage("Dialog returned OK, but no valid device data was stored.");
                StatusText = "Ready.";
            }
        }
        private void ExecuteReInitializeDevice() => StatusText = "Re-initializing device...";
        private bool CanReInitialize() => SelectedDevice is not null;
        private void ExecuteReadProperties() => StatusText = "Reading PCM Properties...";
        private void ExecuteWritePCM() => StatusText = "Writing PCM...";
        private void ExecuteTestWrite() => StatusText = "Running Test Write...";
        private void ExecuteCancelCurrent() => StatusText = "Operation Canceled.";

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
