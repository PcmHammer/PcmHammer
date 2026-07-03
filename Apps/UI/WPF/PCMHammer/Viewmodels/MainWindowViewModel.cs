using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.Services;
using PCMHammer.ViewModels;
using PCMHammer.Views;
using PCMHammer.Views.DialogBoxes;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly MainWindowLogger _logger;
        private readonly FileDialogService _fileDialogService;
        private readonly Window _parentWindow;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool CanReInitialize() => SelectedDevice is not null;

        #region Status and Progress Properties
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
        #endregion

        #region Commands (File Menu)
        public ICommand SaveResultsLogCommand { get; }
        public ICommand SaveDebugLogCommand { get; }
        public ICommand ExitCommand { get; }
        #endregion

        #region Commands (Tools Menu & Operations)
        public ICommand ReadPCMCommand { get; }
        public ICommand VerifyPCMCommand { get; }
        public ICommand ChangeVINCommand { get; }
        public ICommand WriteParametersCommand { get; }
        public ICommand WriteOSCalibrationBootCommand { get; }
        public ICommand WriteFullFlashCloneCommand { get; }
        public ICommand TestFileChecksumsCommand { get; }
        public ICommand BruteForceUnlockCommand { get; }
        public ICommand HaltRunningKernelCommand { get; }
        #endregion

        #region Commands (Options Menu)
        public ICommand UserDefinedKeyCommand { get; }
        public ICommand SettingsCommand { get; }
        #endregion

        #region Commands (Device & Operations Sidebar)
        public ICommand SelectDeviceCommand { get; }
        public ICommand ReInitializeDeviceCommand { get; }
        public ICommand ReadPropertiesCommand { get; }
        public ICommand WritePCMCommand { get; }
        public ICommand TestWriteCommand { get; }
        public ICommand CancelCurrentCommand { get; }
        #endregion

        public MainWindowViewModel(MainWindow parentWindow)
        {
            _parentWindow = parentWindow;
            _logger = new MainWindowLogger(this);
            _fileDialogService = new FileDialogService();
            AddInitialLogMessages();

            // Initialize Commands with actions
            SaveResultsLogCommand = new RelayCommand(execute: async () => await ExecuteSaveResultsLog());
            SaveDebugLogCommand = new RelayCommand(execute: async () => await ExecuteSaveDebugLog());
            ExitCommand = new RelayCommand(ExecuteExit);
            ReadPCMCommand = new RelayCommand(execute: async () => await ExecuteReadPCMAsync(true, PcmType.Undefined));
            VerifyPCMCommand = new RelayCommand(execute: async () => await ExecuteVerificationAsync());
            ChangeVINCommand = new RelayCommand(execute: async () => await ExecuteChangeVINAsync());
            WriteParametersCommand = new RelayCommand(execute: async () => await ExecuteWritePCMAsync(WriteType.Parameters));
            WriteOSCalibrationBootCommand = new RelayCommand(execute: async () => await ExecuteWritePCMAsync(WriteType.OsPlusCalibrationPlusBoot));
            WriteFullFlashCloneCommand = new RelayCommand(execute: async () => await ExecuteWritePCMAsync(WriteType.Full));
            TestFileChecksumsCommand = new RelayCommand(execute: async () => await TestFileChecksumsAsync());
            BruteForceUnlockCommand = new RelayCommand(ExecuteBruteForceUnlock);
            HaltRunningKernelCommand = new RelayCommand(execute: async () => await ExecuteHaltRunningKernel());
            UserDefinedKeyCommand = new RelayCommand(ExecuteUserDefinedKey);
            SettingsCommand = new RelayCommand(ExecuteSettings);
            SelectDeviceCommand = new RelayCommand(() => ExecuteSelectDevice());
            ReInitializeDeviceCommand = new RelayCommand(ExecuteReInitializeDevice, CanReInitialize);
            ReadPropertiesCommand = new RelayCommand(execute: async () => await ExecuteReadPropertiesAsync());
            WritePCMCommand = new RelayCommand(execute: async () => await ExecuteWritePCMAsyncWithDialog());
            TestWriteCommand = new RelayCommand(execute: async () => await ExecuteWritePCMAsync(WriteType.TestWrite));
            CancelCurrentCommand = new RelayCommand(execute: async () => await ExecuteCancelCurrentOperationAsync());

            // Load up saved device and vehicle information from the settings file if configured to do so
            bool retainConfigOnExit = Properties.Settings.Default.RetainDeviceConfigurationOnExit;
            string savedType = Properties.Settings.Default.SavedDeviceType;
            if (retainConfigOnExit && !string.IsNullOrEmpty(savedType))
                _ = TrySilentDeviceConnectionAsync();
        }

        public async Task HandleApplicationShutdownAsync()
        {
            if (Properties.Settings.Default.SaveResultsLogOnExit)
                await Task.Run(() => ExecuteSaveResultsLog());

            if (Properties.Settings.Default.SaveDebugLogOnExit)
                await Task.Run(() => ExecuteSaveDebugLog());
        }

        #region Command Execution Methods
        private async Task ExecuteSaveResultsLog() => await SaveLogFileAsync("UserLog", LogText);
        private async Task ExecuteSaveDebugLog() => await SaveLogFileAsync("DebugLog", DebugLogText);
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
                _cancellationTokenSource = new CancellationTokenSource();

                // Offload processing completely down to the Task Pool thread
                bool success = await Task.Run(() =>
                    _pcmReader.ReadPcmAsync(selectedFilePath, useAutoPcmType, selectedPcmType, _cancellationTokenSource.Token)
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
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // Visual delay to let the user see the complete message status
                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }
        private async Task ExecuteReadPCMAsyncWithDialog()
        {
            WriteOperationDialogBox dialog = new() { Owner = Application.Current.MainWindow };

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
        private async Task ExecuteVerificationAsync()
        {
            if (Vehicle == null) return;
            if (_pcmFlasher == null) return;
            if (IsOperationRunning) return;

            bool useAutoPcmType = true;
            PcmTypeSelectDialogBox dialog = new() { Owner = Application.Current.MainWindow };

            if (dialog.ShowDialog() == true)
            {
                useAutoPcmType = dialog.SelectedPCMType == PcmType.Undefined;
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
                    _cancellationTokenSource = new CancellationTokenSource();

                    PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : dialog.SelectedPCMType;

                    // Offload the low-level communication completely to the worker thread pool
                    bool success = await Task.Run(() =>
                        _pcmFlasher.WritePcmAsync(WriteType.Compare, selectedFilePath, useAutoPcmType, forcedPcmType, _cancellationTokenSource.Token)
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
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

                    await Task.Delay(2000);
                    if (!IsOperationRunning) StatusText = "Ready";
                }
            }
            else
            {
                _logger.AddUserMessage("Comparison canceled by user (dialog closed).");
                StatusText = "Ready";
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
        private async Task TestFileChecksumsAsync()
        {
            // Prompt the user for the .bin file safely on the UI Thread
            StatusText = "Selecting file for validation...";
            string selectedFilePath = _fileDialogService.OpenBinFileDialog()!;

            if (string.IsNullOrEmpty(selectedFilePath))
            {
                StatusText = "Ready";
                return;
            }

            _logger.AddUserMessage($"Examining {selectedFilePath}");
            StatusText = "Validating binary checksums...";

            try
            {
                IsOperationRunning = true;

                // Offload the synchronous disk IO and deep block calculations entirely off the UI thread
                string validationResult = await Task.Run(async () =>
                {
                    // Read the binary image into a memory block byte buffer
                    using Stream stream = File.OpenRead(selectedFilePath);
                    byte[] image = new byte[stream.Length];
                    int bytesRead = await stream.ReadAsync(image, 0, (int)stream.Length);

                    if (bytesRead != stream.Length)
                        return "Error: Unable to fully load file into memory stream context.";

                    // Perform original validation routines from the backend library
                    FileValidator validator = new FileValidator(image, _logger); // Pass your standard shared logger

                    if (validator.IsValid())
                    {
                        string pcmDescription = new OSIDInfo(validator.GetFileType()).Description;
                        return $"File is {pcmDescription}.\r\nAll checksums are valid.";
                    }
                    else
                    {
                        return "This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.";
                    }
                });

                // Post the processed feedback back to the UI logger
                _logger.AddUserMessage(validationResult);
                StatusText = "Validation Complete.";
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Unable to open file: {ex.Message}");
                StatusText = "Error verifying file.";
            }
            finally
            {
                IsOperationRunning = false;

                // Give the UI status layout text a standard brief delay to display completion status
                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }
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
        private async Task ExecuteHaltRunningKernel()
        {
            if (Vehicle == null) return;
            if (IsOperationRunning) return;

            StatusText = "Sending Exit Kernel command...";
            _logger.AddUserMessage("Attempting to exit PCM flash kernel mode...");

            try
            {
                // Lock out the buttons and UI elements automatically via commanding interlocks
                IsOperationRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Offload the low-level bus routine completely down to the Task Pool worker thread
                await Task.Run(async () =>
                {
                    return await Vehicle.ExitKernel(
                        kernelRunning: true,
                        recoveryMode: false,
                        cancellationToken: _cancellationTokenSource.Token,
                        unused: null
                    );
                });

                _logger.AddUserMessage("Exit Kernel command dispatched successfully.");
                StatusText = "PCM Reset Completed.";
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Failed to exit kernel: {ex.Message}");
                _logger.AddDebugMessage(ex.ToString());
                StatusText = "Reset failed.";
            }
            finally
            {
                // Smoothly unlock UI thread control
                IsOperationRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // Visual delay to let the user visually inspect the final status message
                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }
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
            SettingsWindow settingsWindow = new(_fileDialogService) { Owner = _parentWindow };
            StatusText = "Settings...";
            if (settingsWindow.ShowDialog() == true)
                StatusText = "Ready";
        }
        private void ExecuteSelectDevice()
        {
            var pickerDialog = new DevicePickerDialogBox(_logger) { Owner = _parentWindow }; 
            StatusText = "Selecting Device...";

            if (pickerDialog.ShowDialog() == true)
            {

                Device? workingDevice = pickerDialog.SelectedDevice;
                bool enable4xReadWrite = pickerDialog.Enable4xReadWrite;

                if (workingDevice != null)
                {
                    // Pass the device directly into shared configuration helper
                    InitializeDeviceAndVehicle(workingDevice, enable4xReadWrite);
                }
                else
                {
                    _logger.AddDebugMessage("Dialog returned OK, but no valid device data was stored.");
                }
                StatusText = "Ready";
            }
        }
        private async Task<bool> TrySilentDeviceConnectionAsync()
        {
            // Double check safety guards
            if (!Properties.Settings.Default.RetainDeviceConfigurationOnExit ||
                string.IsNullOrEmpty(Properties.Settings.Default.SavedDeviceType))
            {
                return false;
            }

            try
            {
                StatusText = "Restoring saved device configuration...";

                var backgroundViewModel = new DevicePickerViewModel(_logger)
                {
                    DeviceCategory = Properties.Settings.Default.SavedDeviceType
                };

                if (backgroundViewModel.DeviceCategory.Equals("Serial", StringComparison.Ordinal))
                {
                    backgroundViewModel.SerialPort = Properties.Settings.Default.SavedSerialPort;
                    backgroundViewModel.SerialPortDeviceType = Properties.Settings.Default.SavedSerialDevice;
                }
                else
                {
                    backgroundViewModel.J2534DeviceType = Properties.Settings.Default.SavedJ2534Device;
                }

                backgroundViewModel.Enable4xReadWrite = Properties.Settings.Default.SavedDevice4xCommunicationEnabled;

                await backgroundViewModel.TestSelectedDeviceAsync();

                if (backgroundViewModel.SelectedDevice != null)
                {
                    InitializeDeviceAndVehicle(
                        backgroundViewModel.SelectedDevice,
                        backgroundViewModel.Enable4xReadWrite
                    );
                    StatusText = "Ready";
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.AddDebugMessage($"Silent hardware restore failed: {ex.Message}");
            }

            StatusText = "Ready";

            return false; // Failed or timed out; needs UI fallback
        }
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
            InitializeDeviceAndVehicle(SelectedDevice, Vehicle!.Enable4xReadWrite);

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
                    _cancellationTokenSource = new CancellationTokenSource();

                    // Hand off the parameters directly to cleaned-up backend task
                    // Task.Run guarantees it completely leaves the UI thread.
                    bool success = false;
                    if (pcmType == PcmType.Undefined)
                    {
                        success = await Task.Run(() =>
                            _pcmFlasher.WritePcmAsync(writeType, selectedFilePath, useAutoPcmType: true, PcmType.Undefined, _cancellationTokenSource.Token)
                        );
                    }
                    else
                    {
                        success = await Task.Run(() =>
                            _pcmFlasher.WritePcmAsync(writeType, selectedFilePath, useAutoPcmType: false, pcmType, _cancellationTokenSource.Token)
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
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;

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
        private async Task ExecuteCancelCurrentOperationAsync()
        {
            if (IsOperationRunning)
            {
                string warningMessage = "Canceling now could leave your PCM in an unbootable state (bricked)." + Environment.NewLine +
                                 "Are you absolutely sure you want to take that risk?";
                bool proceedWithCancel = await _pcmFlasher!.PromptForYesNo("PCM Hammer", warningMessage);

                if (!proceedWithCancel)
                {
                    _logger.AddUserMessage("Cancellation aborted by user. Continuing operation...");
                    return;
                }
            }

            _logger.AddUserMessage("Cancel button clicked. Signaling background tasks to stop...");
            _cancellationTokenSource?.Cancel();
        }
        #endregion

        #region Private Helpers
        /// <summary>
        /// The core factory mechanism for establishing the bus topology.
        /// </summary>
        private void InitializeDeviceAndVehicle(Device workingDevice, bool Enable4xCom)
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
            )
            {
                Enable4xReadWrite = Enable4xCom
            };

            _logger.AddDebugMessage($"Vehicle pipeline established for: {workingDevice.GetDeviceType()}");

            // Refresh your service layer with the updated Vehicle instance
            _pcmFlasher = new PcmFlasher(Vehicle, _logger);
            _pcmReader = new PcmReader(Vehicle, _logger);
        }
        /// <summary>
        /// This method is used to update the UI with the current status of the operation.
        /// </summary>
        private void AddInitialLogMessages()
        {
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
        }
        /// <summary>
        /// Generates a filename pattern matching the legacy app (e.g., "UserLog_2026-07-01_18-30-00.txt")
        /// </summary>
        private static string GetLogFilename(string logName) => $"{logName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        /// <summary>
        /// Core I/O helper method to write text directly to disk asynchronously.
        /// </summary>
        private async Task SaveLogFileAsync(string logName, string contents)
        {
            if (string.IsNullOrWhiteSpace(contents))
            {
                _logger.AddUserMessage($"Save aborted: {logName} is currently empty.");
                return;
            }

            try
            {
                string defaultName = GetLogFilename(logName);
                string destinationPath = Properties.Settings.Default.UseLogSaveAsDialog ?
                    _fileDialogService.GetLogSavePath(defaultName) : Path.Combine(Properties.Settings.Default.LogDirectory, defaultName);
                
                if (string.IsNullOrEmpty(destinationPath))
                {
                    _logger.AddUserMessage($"{logName} save operation canceled.");
                    return;
                }

                // Completely offload the stream writer to disk from the UI loop
                await File.WriteAllTextAsync(destinationPath, contents);

                _logger.AddUserMessage($"{logName} successfully saved to: {destinationPath}");
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Failed to save log: {ex.Message}");
            }
        }
        #endregion
    }
}
