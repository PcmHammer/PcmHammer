using PCMHammer.Helpers;
using PCMHammer.Views;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // --- Status and Progress Properties ---
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
            // Initialize Commands with placeholder actions
            SaveResultsLogCommand = new RelayCommand(ExecuteSaveResultsLog);
            SaveDebugLogCommand = new RelayCommand(ExecuteSaveDebugLog);
            ExitCommand = new RelayCommand(ExecuteExit);

            ReadPCMCommand = new RelayCommand(ExecuteReadPCM);
            VerifyPCMCommand = new RelayCommand(ExecuteVerifyPCM);
            ChangeVINCommand = new RelayCommand(ExecuteChangeVIN);
            WriteParametersCommand = new RelayCommand(ExecuteWriteParameters);
            WriteOSCalibrationBootCommand = new RelayCommand(ExecuteWriteOSCalibrationBoot);
            WriteFullFlashCloneCommand = new RelayCommand(ExecuteWriteFullFlashClone);
            TestFileChecksumsCommand = new RelayCommand(ExecuteTestFileChecksums);
            BruteForceUnlockCommand = new RelayCommand(ExecuteBruteForceUnlock);
            HaltRunningKernelCommand = new RelayCommand(ExecuteHaltRunningKernel);

            UserDefinedKeyCommand = new RelayCommand(ExecuteUserDefinedKey);
            SettingsCommand = new RelayCommand(ExecuteSettings);

            SelectDeviceCommand = new RelayCommand(ExecuteSelectDevice);
            ReInitializeDeviceCommand = new RelayCommand(ExecuteReInitializeDevice);
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
        private void ExecuteChangeVIN() => MessageBox.Show("Changing VIN...");
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

            DevicePicker dialog = new() { Owner = parentWindow };

            if (dialog.ShowDialog() == true)
            {
                // Here you would normally handle the selected device information from the dialog
            }
        }
        private void ExecuteReInitializeDevice() => StatusText = "Re-initializing device...";
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
