using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.Services;
using PCMHammer.ViewModels;
using PCMHammer.Views;
using PCMHammer.Views.DialogBoxes;
using System.Configuration;
using System.Windows;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly MainWindowLogger _logger;
        private readonly FileDialogService _fileDialogService;
        private readonly Window _parentWindow;
        private bool CanReInitialize() => SelectedDevice is not null;

        // --- Status and Progress Properties ---
        private PcmFlasher? _pcmFlasher;
        public PcmFlasher? PcmFlasher
        {
            get => _pcmFlasher;
            set => SetProperty(ref _pcmFlasher, value);
        }

        private PcmReader? _pcmReader;
        public PcmReader? PcmReader
        {
            get => _pcmReader;
            set => SetProperty(ref _pcmReader, value);
        }

        private Device? _selectedDevice;
        public Device? SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        private string _logText = string.Empty;
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        private string _debugLogText = string.Empty;
        public string DebugLogText
        {
            get => _debugLogText;
            set => SetProperty(ref _debugLogText, value);
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private int _retryCount = 0;
        public int RetryCount
        {
            get => _retryCount;
            set => SetProperty(ref _retryCount, value);
        }

        private double _transferRate = 0.0;
        public double TransferRate
        {
            get => _transferRate;
            set => SetProperty(ref _transferRate, value);
        }

        private double _progressPercent = 0.0;
        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetProperty(ref _progressPercent, value);
        }

        private string _timeRemaining = "00:00 Remaining";
        public string TimeRemaining
        {
            get => _timeRemaining;
            set => SetProperty(ref _timeRemaining, value);
        }

        private Vehicle? _vehicle;
        public Vehicle? Vehicle
        {
            get => _vehicle;
            set
            {
                if (SetProperty(ref _vehicle, value))
                    RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _isOperationRunning;
        public bool IsOperationRunning
        {
            get => _isOperationRunning;
            set => SetProperty(ref _isOperationRunning, value);
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

        public MainWindowViewModel(MainWindow parentWindow)
        {
            _parentWindow = parentWindow;
            _logger = new MainWindowLogger(this);
            _fileDialogService = new FileDialogService();
            // Add user messages to the log
            _logger.AddUserMessage("PCM Hammer");
            _logger.AddUserMessage("Copyright (C) 2018-2026 PcmHacking.net - GPL v3");
            _logger.AddUserMessage("Version: 2.0.0");
            _logger.AddUserMessage($"Running at: {DateTime.Now:dddd, MMMM d yyyy, HH:mm:ss}");
            _logger.AddUserMessage("Thanks for using PCM Hammer.");
            // Add debug messages to the debug log
            _logger.AddDebugMessage("PCM Hammer");
            _logger.AddDebugMessage("Copyright (C) 2018-2026 PcmHacking.net - GPL v3");
            _logger.AddDebugMessage("Version: 2.0.0");
            _logger.AddDebugMessage($"Running at: {DateTime.Now:dddd, MMMM d yyyy, HH:mm:ss}");
            _logger.AddDebugMessage("Thanks for using PCM Hammer.");

            // Initialize Commands with actions
            SaveResultsLogCommand = new RelayCommand(ExecuteSaveResultsLog);
            SaveDebugLogCommand = new RelayCommand(ExecuteSaveDebugLog);
            ExitCommand = new RelayCommand(ExecuteExit);

            ReadPCMCommand = new RelayCommand(
                execute: async () => await ExecuteReadPCMAsync(true, PcmType.Undefined)
            );
            VerifyPCMCommand = new RelayCommand(
                execute: async () => await ExecuteVerificationAsync(true, PcmType.Undefined)
            );
            ChangeVINCommand = new RelayCommand(
                execute: async () => await ExecuteChangeVINAsync()
            );
            WriteParametersCommand = new RelayCommand(
                execute: async () => await ExecuteWritePCMAsync(WriteType.Parameters)
            );
            WriteOSCalibrationBootCommand = new RelayCommand(
                execute: async () => await ExecuteWritePCMAsync(WriteType.OsPlusCalibrationPlusBoot)
            );
            WriteFullFlashCloneCommand = new RelayCommand(
                execute: async () => await ExecuteWritePCMAsync(WriteType.Full)
            );
            TestFileChecksumsCommand = new RelayCommand(ExecuteTestFileChecksums);
            BruteForceUnlockCommand = new RelayCommand(ExecuteBruteForceUnlock);
            HaltRunningKernelCommand = new RelayCommand(ExecuteHaltRunningKernel);

            UserDefinedKeyCommand = new RelayCommand(ExecuteUserDefinedKey);
            SettingsCommand = new RelayCommand(ExecuteSettings);

            SelectDeviceCommand = new RelayCommand(ExecuteSelectDevice);
            ReInitializeDeviceCommand = new RelayCommand(ExecuteReInitializeDevice, CanReInitialize);
            ReadPropertiesCommand = new RelayCommand(
                execute: async () => await ExecuteReadPropertiesAsync()
            );
            WritePCMCommand = new RelayCommand(
                execute: async () => await ExecuteWritePCMAsyncWithDialog()
            );
            TestWriteCommand = new RelayCommand(
                execute: async () => await ExecuteWritePCMAsync(WriteType.TestWrite)
            );
            CancelCurrentCommand = new RelayCommand(ExecuteCancelCurrent);
        }

        // --- Command Execution Methods ---
        private void ExecuteSaveResultsLog() => MessageBox.Show("Saving Results Log...");
        private void ExecuteSaveDebugLog() => MessageBox.Show("Saving Debug Log...");
        private void ExecuteExit() => Application.Current.Shutdown();
        private async Task ExecuteReadPCMAsync(bool useAutoPcmType, PcmType selectedPcmType)
        {
            if (Vehicle == null) return;
            if (_pcmReader == null) return;
            if (IsOperationRunning) return;

            StatusText = "Preparing for Read...";

            // Get save destination on the UI thread before calling Task.Run
            string selectedFilePath = _fileDialogService.SaveBinFileDialog()!;

            if (string.IsNullOrEmpty(selectedFilePath))
            {
                _logger.AddUserMessage("Read operation canceled by user (no file selected).");
                StatusText = "Ready";
                return;
            }

            try
            {
                IsOperationRunning = true;
                StatusText = "Reading PCM Contents...";
                var cts = new CancellationTokenSource();

                // Offload processing completely down to the Task Pool thread
                bool success = await Task.Run(() =>
                    _pcmReader.ReadPcmAsync(selectedFilePath, useAutoPcmType, selectedPcmType, cts.Token)
                );

                StatusText = success ? "Read Completed Successfully!" : "Read Failed.";
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Critical error during read: {ex.Message}");
                StatusText = "Error occurred.";
            }
            finally
            {
                IsOperationRunning = false;

                // Visual delay to let the user see the complete message status
                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }
        public async Task ExecuteReadPCMAsyncWithDialog()
        {
            WriteOperationDialogBox dialog = new() { Owner = System.Windows.Application.Current.MainWindow };

            WriteTypeViewModel viewModel = new()
            {
                SelectedWriteType = WriteType.Full,
                SelectedPCMType = PcmType.Undefined
            };

            viewModel.RequestClose += () => dialog.DialogResult = false;
            viewModel.RequestAcceptandClose += () => dialog.DialogResult = true;

            dialog.DataContext = viewModel;

            if (dialog.ShowDialog() == true)
            {
                bool useAutoPcmType = viewModel.SelectedPCMType == PcmType.Undefined;
                await ExecuteReadPCMAsync(useAutoPcmType, viewModel.SelectedPCMType);
            }
        }
        private async Task ExecuteVerificationAsync(bool useAutoPcmType, PcmType selectedPcmType)
        {
            if (Vehicle == null) return;
            if (_pcmFlasher == null) return;
            if (IsOperationRunning) return;

            // Set appropriate status messages depending on the action type
            StatusText = "Preparing for Comparison...";

            // Get the source binary path safely on the UI thread before offloading
            string selectedFilePath = _fileDialogService.OpenBinFileDialog()!;

            if (string.IsNullOrEmpty(selectedFilePath))
            {
                string cancelMessage = "Comparison canceled.";
                _logger.AddUserMessage(cancelMessage);
                StatusText = "Ready";
                return;
            }

            try
            {
                IsOperationRunning = true;
                StatusText = "Comparing PCM Blocks...";

                var cts = new CancellationTokenSource();
                PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : selectedPcmType;

                // Offload the low-level communication completely to the worker thread pool
                bool success = await Task.Run(() =>
                    _pcmFlasher.WritePcmAsync(WriteType.Compare, selectedFilePath, useAutoPcmType, forcedPcmType, cts.Token)
                );

                if (success)
                {
                    StatusText = "Comparison Complete!";
                }
                else
                {
                    StatusText = "Comparison Found Differences or Failed.";
                }
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Critical error during verification: {ex.Message}");
                StatusText = "Error occurred.";
            }
            finally
            {
                // Smoothly restore UI control structures
                IsOperationRunning = false;

                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }
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

                var vinViewModel = new ChangeVinViewModel(vinResponse.Value);
                var vinDialog = new ChangeVinDialogBox(vinViewModel) { Owner = _parentWindow };

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
        private void ExecuteTestFileChecksums() => MessageBox.Show("Testing Checksums...");
        private void ExecuteBruteForceUnlock()
        {
            StatusText = "Brute Force Unlocking...";
            if (Vehicle == null) return;
            BruteForceDialogBox bruteForceDialog = new(vehicle: Vehicle, logger: _logger) { Owner = _parentWindow };
            if (bruteForceDialog.ShowDialog() == true)
            {
                StatusText = "Brute Force Unlock Completed.";
            }
            else
            {
                StatusText = "Ready";
            }
        }
        private void ExecuteHaltRunningKernel() => StatusText = "Kernel Halted.";
        private void ExecuteUserDefinedKey()
        {
            UserDefinedKeyDialogBox userDefinedKeyDialog = new() { Owner = _parentWindow };
            StatusText = "Setting user-defined key...";
            if (userDefinedKeyDialog.ShowDialog() == true)
            {
                StatusText = "Ready";
            }
        }
        private void ExecuteSettings()
        {
            SettingsWindow settingsWindow = new() { Owner = _parentWindow };
            StatusText = "Settings...";
            if (settingsWindow.ShowDialog() == true)
                StatusText = "Ready";
        }
        /// <summary>
        /// Opens the UI dialog box to select a physical device from scratch.
        /// </summary>
        private void ExecuteSelectDevice()
        {
            var pickerDialog = new DevicePickerDialogBox(_logger) { Owner = _parentWindow };
            StatusText = "Selecting Device...";

            if (pickerDialog.ShowDialog() == true)
            {
                Device? workingDevice = (pickerDialog.DataContext as DevicePickerViewModel)?.SelectedDevice;

                if (workingDevice != null)
                {
                    // Pass the device directly into our new shared configuration helper
                    InitializeDeviceAndVehicle(workingDevice);
                }
                else
                {
                    _logger.AddDebugMessage("Dialog returned OK, but no valid device data was stored.");
                }
                StatusText = "Ready";
            }
        }
        /// <summary>
        /// Silently re-initializes the vehicle bus communication using the current selection.
        /// </summary>
        private void ExecuteReInitializeDevice()
        {
            if (SelectedDevice == null)
            {
                _logger.AddUserMessage("Cannot re-initialize: No device has been selected yet.");
                return;
            }

            StatusText = "Re-initializing device communication...";
            _logger.AddDebugMessage($"Re-initializing link to: {SelectedDevice.GetDeviceType()}");

            // Reuse the exact same connection architecture silently
            InitializeDeviceAndVehicle(SelectedDevice);

            StatusText = "Ready";
        }
        private async Task ExecuteReadPropertiesAsync()
        {
            StatusText = "Reading Properties...";
            if (Vehicle == null) return;
            try
            {
                IsOperationRunning = true;
                await Task.Run(async () =>
                {
                    OSIDInfo? pcmInfo = null;

                    var vinResponse = await Vehicle.QueryVin();
                    if (vinResponse.Status != ResponseStatus.Success)
                    {
                        _logger.AddUserMessage($"VIN query failed: {vinResponse.Status}");
                        await Vehicle.ExitKernel();
                        return;
                    }
                    _logger.AddUserMessage($"VIN: {vinResponse.Value}");

                    var osResponse = await Vehicle.QueryOperatingSystemId(new CancellationToken());
                    if (osResponse.Status == ResponseStatus.Success)
                    {
                        _logger.AddUserMessage($"OSID: {osResponse.Value}");
                        pcmInfo = new OSIDInfo(osResponse.Value);
                        _logger.AddUserMessage($"Description: {pcmInfo.Description}");
                    }
                    else
                        _logger.AddUserMessage($"OS ID query failed: {osResponse.Status}");

                    // Disable Calibration ID lookup for those that do not provide it
                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
                    {
                        var calResponse = await Vehicle.QueryCalibrationId();
                        if (calResponse.Status == ResponseStatus.Success)
                            _logger.AddUserMessage($"Calibration ID: {calResponse.Value}");
                        else
                            _logger.AddUserMessage($"Calibration ID query failed: {calResponse.Status}");
                    }

                    // Disable HardwareID lookup for the P05, P10, P12 and E54.
                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P05 &&
                        pcmInfo.HardwareType != PcmType.P05b && pcmInfo.HardwareType != PcmType.P10 &&
                        pcmInfo.HardwareType != PcmType.P12 && pcmInfo.HardwareType != PcmType.E54)
                    {
                        var hardwareResponse = await Vehicle.QueryHardwareId();
                        if (hardwareResponse.Status == ResponseStatus.Success)
                            _logger.AddUserMessage($"Hardware ID: {hardwareResponse.Value}");
                        else
                            _logger.AddUserMessage($"Hardware ID query failed: {hardwareResponse.Status}");
                    }

                    // Disable Serial Number lookup for those that do not provide it
                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
                    {
                        var serialResponse = await Vehicle.QuerySerial();
                        if (serialResponse.Status == ResponseStatus.Success)
                            _logger.AddUserMessage($"Serial Number: {serialResponse.Value}");
                        else
                            _logger.AddUserMessage($"Serial Number query failed: {serialResponse.Status}");
                    }

                    // Disable BCC lookup for those that do not provide it
                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P04 &&
                        pcmInfo.HardwareType != PcmType.P04_Early && pcmInfo.HardwareType != PcmType.P08)
                    {
                        var bccResponse = await Vehicle.QueryBCC();
                        if (bccResponse.Status == ResponseStatus.Success)
                            _logger.AddUserMessage($"Broad Cast Code: {bccResponse.Value}");
                        else
                            _logger.AddUserMessage($"BCC query failed: {bccResponse.Status}");
                    }

                    var mecResponse = await Vehicle.QueryMEC();
                    if (mecResponse.Status == ResponseStatus.Success)
                        _logger.AddUserMessage($"MEC: {mecResponse.Value}");
                    else
                        _logger.AddUserMessage($"MEC query failed: {mecResponse.Status}");

                    var voltageResponse = await Vehicle.QueryVoltage();
                    if (voltageResponse.Status == ResponseStatus.Success)
                        _logger.AddUserMessage($"Voltage: {voltageResponse.Value}");
                    else
                        _logger.AddUserMessage($"Voltage query failed: {voltageResponse.Status}");
                });
            }
            catch (Exception exception)
            {
                _logger.AddUserMessage(exception.Message);
                _logger.AddDebugMessage(exception.ToString());
            }
            finally
            {
                IsOperationRunning = false;
                StatusText = "Ready";
            }
        }
        private async Task ExecuteWritePCMAsync(WriteType writeType, PcmType pcmType = PcmType.Undefined)
        {
            if (Vehicle == null) return;
            if (_pcmFlasher == null) return;
            if (IsOperationRunning) return;

            DelayDialogBox delayDialog = new() { Owner = Application.Current.MainWindow };
            StatusText = "Writing to PCM...";

            if (delayDialog.ShowDialog() == true)
            {
                string selectedFilePath = _fileDialogService.OpenBinFileDialog()!;

                if (string.IsNullOrEmpty(selectedFilePath))
                {
                    _logger.AddUserMessage("Flash operation canceled by user (no file selected).");
                    StatusText = "Ready";
                    return;
                }

                try
                {
                    IsOperationRunning = true;
                    StatusText = "Writing to PCM...";
                    var cts = new CancellationTokenSource();

                    // Hand off the parameters directly to cleaned-up backend task
                    // Task.Run guarantees it completely leaves the UI thread.
                    bool success = false;
                    if (pcmType == PcmType.Undefined)
                    {
                        success = await Task.Run(() =>
                            _pcmFlasher.WritePcmAsync(writeType, selectedFilePath, useAutoPcmType: true, PcmType.Undefined, cts.Token)
                        );
                    }
                    else
                    {
                        success = await Task.Run(() =>
                            _pcmFlasher.WritePcmAsync(writeType, selectedFilePath, useAutoPcmType: false, pcmType, cts.Token)
                        );
                    }

                    StatusText = success ? "Write Complete Successfully!" : "Write Failed.";
                }
                catch (Exception ex)
                {
                    _logger.AddUserMessage($"Critical error: {ex.Message}");
                    StatusText = "Error occurred.";
                }
                finally
                {
                    // Always restore UI state
                    IsOperationRunning = false;

                    await Task.Delay(2000);
                    if (!IsOperationRunning) StatusText = "Ready";
                }
            }
        }
        private async Task ExecuteWritePCMAsyncWithDialog()
        {
            WriteOperationDialogBox dialog = new() { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() == true)
            {
                await ExecuteWritePCMAsync(dialog.SelectedWriteType, dialog.SelectedPCMType);
            }
        }
        private void ExecuteCancelCurrent() => StatusText = "Operation Canceled.";
        /// <summary>
        /// Shared Helper: The core factory mechanism for establishing the bus topology.
        /// </summary>
        private void InitializeDeviceAndVehicle(Device workingDevice)
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

            _logger.AddDebugMessage($"Vehicle pipeline established for: {workingDevice.GetDeviceType()}");

            // Refresh your service layer with the updated Vehicle instance
            _pcmFlasher = new PcmFlasher(Vehicle, _logger);
            _pcmReader = new PcmReader(Vehicle, _logger);
        }
    }
}
