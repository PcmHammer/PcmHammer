using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Services;
using PCMHammer.Views;
using PCMHammer.Views.DialogBoxes;
using System.IO;
using System.IO.Ports;
using System.Windows;

namespace PCMHammer.Viewmodels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        #region Fields
        private readonly PCMHammer.Helpers.MainWindowLogger _logger;
        private readonly FileDialogService _fileDialogService;
        private readonly Window _parentWindow;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool CanReInitialize() => SelectedDevice is not null && !IsOperationRunning;
        #endregion

        #region Properties
        [ObservableProperty]
        public partial PcmFlasher? PcmFlasher { get; set; }

        [ObservableProperty]
        public partial PcmReader? PcmReader { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartOperation))]
        [NotifyPropertyChangedFor(nameof(CanWriteDocument))]
        [NotifyCanExecuteChangedFor(nameof(ReInitializeDeviceCommand))]
        public partial Device? SelectedDevice { get; set; }

        [ObservableProperty]
        public partial string LogText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string DebugLogText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsCopiedFeedbackVisible { get; set; }

        [ObservableProperty]
        public partial string StatusText { get; set; } = "Ready";

        [ObservableProperty]
        public partial int RetryCount { get; set; } = 0;

        [ObservableProperty]
        public partial double TransferRate { get; set; } = 0.0;

        [ObservableProperty]
        public partial double ProgressPercent { get; set; } = 0.0;

        [ObservableProperty]
        public partial string TimeRemaining { get; set; } = "00:00 Remaining";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DeviceDescription))]
        public partial Vehicle? Vehicle { get; set; }

        /// <summary>
        /// The connected interface, shown on the main window the way WinForms shows it in its
        /// Device box. The text comes from the library (Vehicle.DeviceDescription -> Device
        /// .ToString()), which is the same source MainFormBase hands to WinForms, so every front
        /// end names a device identically and none of them format it themselves.
        /// </summary>
        public string DeviceDescription => Vehicle?.DeviceDescription ?? "No device selected.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartOperation))]
        [NotifyPropertyChangedFor(nameof(IsDeviceControlEnabled))]
        [NotifyPropertyChangedFor(nameof(CanWriteDocument))]
        [NotifyPropertyChangedFor(nameof(CanUseDocument))]
        public partial bool IsOperationRunning { get; set; }

        /// <summary>
        /// True when a new operation may be started: a device is connected and nothing is running.
        /// The Operations buttons and the operation menu items bind their IsEnabled to this, so they
        /// grey out for the duration of a read/write/verify instead of inviting a second command.
        /// </summary>
        public bool CanStartOperation => SelectedDevice is not null && !IsOperationRunning;

        /// <summary>
        /// True when device-level controls (Select / Re-Initialize, file loading) are allowed: simply
        /// when no operation is running. These do not require a device to already be present.
        /// </summary>
        public bool IsDeviceControlEnabled => !IsOperationRunning;

        #endregion

        #region Command state

        /// <summary>
        /// Re-evaluate the CanExecute of every gated command. Called whenever the state those predicates
        /// read (connected device, operation-running, loaded document) changes, so the bound menu items
        /// and buttons enable/disable themselves - each command owns its rule and the views just reflect
        /// it, instead of every view repeating an IsEnabled binding.
        /// </summary>
        private void RefreshCommandStates()
        {
            SelectDeviceCommand.NotifyCanExecuteChanged();
            ReInitializeDeviceCommand.NotifyCanExecuteChanged();
            LoadFileCommand.NotifyCanExecuteChanged();
            SaveFileCommand.NotifyCanExecuteChanged();
            SaveFileAsCommand.NotifyCanExecuteChanged();
            ImportModuleCommand.NotifyCanExecuteChanged();
            ExportBinCommand.NotifyCanExecuteChanged();
            ReadPropertiesCommand.NotifyCanExecuteChanged();
            ReadPCMCommand.NotifyCanExecuteChanged();
            WritePCMCommand.NotifyCanExecuteChanged();
            TestWriteCommand.NotifyCanExecuteChanged();
            VerifyPCMCommand.NotifyCanExecuteChanged();
            WriteParametersCommand.NotifyCanExecuteChanged();
            WriteOSCalibrationBootCommand.NotifyCanExecuteChanged();
            WriteFullFlashCloneCommand.NotifyCanExecuteChanged();
            ChangeVINCommand.NotifyCanExecuteChanged();
            RecoveryReadCommand.NotifyCanExecuteChanged();
            RecoveryWriteCommand.NotifyCanExecuteChanged();
            TestFileChecksumsCommand.NotifyCanExecuteChanged();
            BruteForceUnlockCommand.NotifyCanExecuteChanged();
            HaltRunningKernelCommand.NotifyCanExecuteChanged();
            UserDefinedKeyCommand.NotifyCanExecuteChanged();
            CancelCurrentCommand.NotifyCanExecuteChanged();
        }

        // CommunityToolkit generates these hooks; each fires when its property changes. Refreshing the
        // command states here keeps every command's enabled state current (see RefreshCommandStates).
        partial void OnSelectedDeviceChanged(Device? value) => RefreshCommandStates();
        partial void OnIsOperationRunningChanged(bool value) => RefreshCommandStates();
        partial void OnLoadedPackageChanged(PcmPackage? value) => RefreshCommandStates();

        #endregion

        #region Commands
        [RelayCommand]
        public async Task CopyLog(string logText)
        {
            if (string.IsNullOrEmpty(logText)) return;

            Clipboard.SetText(logText);

            // Trigger visual feedback
            IsCopiedFeedbackVisible = true;

            // Wait 1 second, then hide the feedback
            await Task.Delay(1000);

            IsCopiedFeedbackVisible = false;
        }

        #region Commands (File Menu)
        [RelayCommand]
        public async Task SaveResultsLog() => await SaveLogFileAsync("UserLog", LogText);
        [RelayCommand]
        public async Task SaveDebugLog() => await SaveLogFileAsync("DebugLog", DebugLogText);
        [RelayCommand]
        public static void Exit() => Application.Current.Shutdown();
        #endregion

        #region Commands (Tools Menu & Operations)
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public async Task ReadPCM() => await ExecuteReadPCMAsync(true, PcmType.Undefined);
        [RelayCommand(CanExecute = nameof(CanWriteDocument))]
        public async Task VerifyPCM() => await ExecuteVerificationAsync();
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public async Task ChangeVIN() => await ExecuteChangeVINAsync();
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public async Task RecoveryRead() => await ExecuteRecoveryReadAsync();
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public async Task RecoveryWrite() => await ExecuteRecoveryWriteAsync();
        [RelayCommand(CanExecute = nameof(CanWriteDocument))]
        public async Task WriteParameters() => await ExecuteWritePCMAsync(WriteType.Parameters);
        [RelayCommand(CanExecute = nameof(CanWriteDocument))]
        public async Task WriteOSCalibrationBoot() => await ExecuteWritePCMAsync(WriteType.OsPlusCalibrationPlusBoot);
        [RelayCommand(CanExecute = nameof(CanWriteDocument))]
        public async Task WriteFullFlashClone() => await ExecuteWritePCMAsync(WriteType.Full);
        [RelayCommand(CanExecute = nameof(IsDeviceControlEnabled))]
        public async Task TestFileChecksums() => await TestFileChecksumsAsync();
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public void BruteForceUnlock()
        {
            StatusText = "Brute Force Unlocking...";
            if (Vehicle == null) return;
            BruteForceDialogBox bruteForceDialog = new(vehicle: Vehicle, logger: _logger) { Owner = _parentWindow };
            StatusText = bruteForceDialog.ShowDialog() == true ? "Brute Force Unlock Completed." : "Ready";
        }
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public async Task HaltRunningKernel()
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
        #endregion

        #region Commands (Options Menu)
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public void UserDefinedKey()
        {
            UserDefinedKeyDialogBox userDefinedKeyDialog = new() { Owner = _parentWindow };
            StatusText = "Setting user-defined key...";
            if (userDefinedKeyDialog.ShowDialog() == true)
            {
                StatusText = "Ready";
            }
        }
        [RelayCommand]
        public void Settings() 
        {
            SettingsWindow settingsWindow = new(_fileDialogService) { Owner = _parentWindow };
            StatusText = "Settings...";
            if (settingsWindow.ShowDialog() == true)
                StatusText = "Ready";

            // The settings dialog can toggle RuntimeSettings.AllowModuleImport; refresh the menu binding.
            OnPropertyChanged(nameof(IsModuleImportAllowed));
        }
        #endregion

        #region Commands (Device & Operations Sidebar)
        [RelayCommand(CanExecute = nameof(IsDeviceControlEnabled))]
        public void SelectDevice()
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
        [RelayCommand(CanExecute=nameof(CanReInitialize))]
        public void ReInitializeDevice() 
        {
            if (SelectedDevice == null)
            {
                _logger.AddUserMessage("Cannot re-initialize: No device has been selected yet.");
                return;
            }

            StatusText = "Re-initializing device communication...";
            _logger.AddDebugMessage(message: $"Re-initializing link to: {SelectedDevice.GetDeviceType()}");

            // Reuse the exact same connection architecture silently
            InitializeDeviceAndVehicle(SelectedDevice, Vehicle!.Enable4xReadWrite);

            StatusText = "Ready";
        }
        [RelayCommand(CanExecute = nameof(CanStartOperation))]
        public async Task ReadProperties()
        {
            StatusText = "Reading Properties...";
            if (Vehicle == null) return;
            try
            {
                IsOperationRunning = true;
                await Task.Run(async () =>
                {
                    // One shared flow detects the bus (VPW or CAN) and reads the identification; the
                    // UI only logs what it returns.
                    PcmIdentity? identity = await Vehicle.ReadIdentity(CancellationToken.None);
                    if (identity == null)
                    {
                        _logger.AddUserMessage("No PCM detected.");
                        return;
                    }

                    foreach (string line in identity.Lines)
                    {
                        _logger.AddUserMessage(line);
                    }
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
        [RelayCommand(CanExecute = nameof(CanWriteDocument))]
        public async Task WritePCM() => await ExecuteWritePCMAsyncWithDialog();
        [RelayCommand(CanExecute = nameof(CanWriteDocument))]
        public async Task TestWrite() => await ExecuteWritePCMAsync(WriteType.TestWrite);
        [RelayCommand(CanExecute = nameof(IsOperationRunning))]
        public async Task CancelCurrent()
        {
            if (IsOperationRunning)
            {
                string warningMessage = "Canceling now could leave your PCM in an unbootable state (bricked)." + Environment.NewLine +
                                 "Are you absolutely sure you want to take that risk?";
                bool proceedWithCancel = await PcmFlasher!.PromptForYesNo(warningMessage, "PCM Hammer");

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

        #endregion

        public MainWindowViewModel(MainWindow parentWindow)
        {
            _parentWindow = parentWindow;
            _logger = new PCMHammer.Helpers.MainWindowLogger(this);
            _fileDialogService = new FileDialogService();
            _logger.ProgressBarUpdated += (percent, visible) =>
            {
                // Safely hop onto the WPF UI thread to update properties
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // WriteManager/Reader sends percentages as decimals (0.0 to 1.0)
                    // Multiply by 100 to map onto a standard 0-100 ProgressBar
                    ProgressPercent = percent * 100;
                });
            };
            AddInitialLogMessages();

            // Reconnect to the last-used interface on startup when the user has left that option on
            // (the default). The device is saved whenever one is picked; here we default straight back
            // to it, falling back silently to the "no device" state if it can't be opened (e.g. unplugged).
            string savedType = Properties.Settings.Default.SavedDeviceType;
            if (Properties.Settings.Default.RetainDeviceConfigurationOnExit && !string.IsNullOrEmpty(savedType))
                _ = TrySilentDeviceConnectionAsync();
        }

        public async Task HandleApplicationShutdownAsync()
        {
            // The logger batches messages and flushes them on a timer; flush now (we are on the UI
            // thread) so the logs we save below include every message up to this point.
            _logger.Flush();

            var tasks = new List<Task>();

            if (Properties.Settings.Default.SaveResultsLogOnExit && SaveResultsLogCommand.CanExecute(null))
                tasks.Add(SaveLogFileAsync("UserLog", LogText));

            if (Properties.Settings.Default.SaveDebugLogOnExit && SaveDebugLogCommand.CanExecute(null))
                tasks.Add(SaveLogFileAsync("DebugLog", DebugLogText));

            if (tasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (Exception)
                {
                    _logger.AddDebugMessage("One or more log save operations failed during application shutdown.");
                }
            }

            // Release the hardware interface, the way WinForms disposes its Vehicle on close. Without
            // this the J2534 device is never closed (PassThruDisconnect/PassThruClose never run), so it
            // keeps its channel open and the next launch cannot reopen it - e.g. "failed to open
            // ISO15765 (CAN) channel, error 0x1B". Vehicle.Dispose only closes the underlying device
            // once its ShutdownSignalSource is cancelled (a reuse hook for the Uno front end), so signal
            // that first.
            try
            {
                Vehicle?.ShutdownSignalSource.Cancel();
                Vehicle?.Dispose();
                Vehicle = null;
            }
            catch (Exception exception)
            {
                _logger.AddDebugMessage("Device cleanup on shutdown failed: " + exception.Message);
            }

            // Stop the logger's flush timer and do its final flush (the logger implements IDisposable
            // for exactly this). Last so any messages from the cleanup above are still captured.
            _logger.Dispose();
        }

        #region Command Execution Methods
        private async Task ExecuteReadPCMAsync(bool useAutoPcmType, PcmType selectedPcmType)
        {
            if (Vehicle == null) return;
            if (PcmReader == null) return;
            if (IsOperationRunning) return;

            // Read into the in-memory working document (unsaved), the way WinForms does: no file is
            // chosen up front. The user saves it via the prompt afterwards or the Save button, and Write
            // flashes it directly - no file needed in between. Discard-check first so a fresh read never
            // silently overwrites unsaved changes to the current document.
            if (!ConfirmDiscardIfDirty())
            {
                return;
            }

            try
            {
                IsOperationRunning = true;
                StatusText = "Reading PCM Contents...";
                _cancellationTokenSource = new CancellationTokenSource();

                // Offload processing completely down to the Task Pool thread
                PcmPackage? package = await Task.Run(() =>
                    PcmReader.ReadToPackageAsync(useAutoPcmType, selectedPcmType, _cancellationTokenSource.Token)
                );

                if (package != null)
                {
                    SetLoadedPackage(package, path: null, dirty: true);
                    StatusText = "Read Completed Successfully!";
                    PromptSaveAfterRead();
                }
                else
                {
                    StatusText = "Read Failed.";
                }
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
            if (PcmFlasher == null) return;
            if (IsOperationRunning) return;

            // Verify compares the loaded working document against what is on the PCM - no file is picked.
            PcmPackage? document = LoadedPackage;
            if (document == null)
            {
                _logger.AddUserMessage("No file loaded. Read the PCM or load a file first.");
                return;
            }

            PcmTypeSelectDialogBox dialog = new() { Owner = Application.Current.MainWindow };

            if (dialog.ShowDialog() == true)
            {
                bool useAutoPcmType = dialog.SelectedPCMType == PcmType.Undefined;
                PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : dialog.SelectedPCMType;

                try
                {
                    IsOperationRunning = true;
                    StatusText = "Comparing PCM Blocks...";
                    _cancellationTokenSource = new CancellationTokenSource();

                    // Offload the low-level communication completely to the worker thread pool
                    bool success = await Task.Run(() =>
                        PcmFlasher.WritePackageAsync(WriteType.Compare, document, useAutoPcmType, forcedPcmType, _cancellationTokenSource.Token)
                    );

                    StatusText = success ? "Comparison Complete!" : "Comparison Found Differences or Failed.";
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
                    // No cancellation source in the VIN flow; the unlock is bounded by its own time budget.
                    bool unlocked = await Vehicle.UnlockEcu(info.KeyAlgorithm, CancellationToken.None);
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
                    int bytesRead = await stream.ReadAsync(image.AsMemory(0, (int)stream.Length));

                    if (bytesRead != stream.Length)
                        return "Error: Unable to fully load file into memory stream context.";

                    // Perform original validation routines from the backend library
                    FileValidator validator = new(image, _logger); // Pass your standard shared logger

                    if (validator.IdentifyAndValidate())
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
        private async Task<bool> TrySilentDeviceConnectionAsync()
        {
            // Respect the user's choice, and do nothing until a device has been picked at least once.
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
                    backgroundViewModel.SerialPort = backgroundViewModel.SerialPorts.FirstOrDefault(p => p.PortName == Properties.Settings.Default.SavedSerialPort); 
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

                    // Do NOT call AcceptCommand here: it re-runs TestSelectedDeviceAsync, which opens a
                    // SECOND device that is never wrapped or disposed (a leaked handle, and a second
                    // PassThruOpen on the same J2534 hardware). The settings we are restoring from are
                    // already saved, so there is nothing to persist.
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
        private async Task ExecuteWritePCMAsync(WriteType writeType, PcmType pcmType = PcmType.Undefined)
        {
            if (Vehicle == null) return;
            if (PcmFlasher == null) return;
            if (IsOperationRunning) return;

            // Flash the in-memory working document; the bytes come from the loaded package, not a file.
            PcmPackage? document = LoadedPackage;
            if (document == null)
            {
                _logger.AddUserMessage("No file loaded. Read the PCM or load a file first.");
                return;
            }

            DelayDialogBox delayDialog = new() { Owner = Application.Current.MainWindow };
            StatusText = "Writing to PCM...";

            if (delayDialog.ShowDialog() == true)
            {
                try
                {
                    IsOperationRunning = true;
                    StatusText = "Writing to PCM...";
                    _cancellationTokenSource = new CancellationTokenSource();

                    _logger.AddUserMessage("Writing the loaded file" +
                        (LoadedPackagePath != null ? " (" + Path.GetFileName(LoadedPackagePath) + ")" : string.Empty) + ".");

                    bool useAutoPcmType = pcmType == PcmType.Undefined;

                    // Task.Run guarantees it completely leaves the UI thread.
                    bool success = await Task.Run(() =>
                        PcmFlasher.WritePackageAsync(writeType, document, useAutoPcmType, pcmType, _cancellationTokenSource.Token)
                    );

                    StatusText = success ? "Write Operation Completed." : "Write Operation Failed.";
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
        /// <summary>
        /// PCM Recovery -> Read. Asks which PCM is on the bench, then reads it straight through the
        /// recovery path (no detection). The result replaces the working document, exactly like a
        /// normal read.
        /// </summary>
        private async Task ExecuteRecoveryReadAsync()
        {
            if (Vehicle == null || PcmReader == null || IsOperationRunning) return;

            PcmType? pcmType = PromptForRecoveryPcmType();
            if (pcmType == null) return;

            // Same document rules as a normal read: don't silently discard unsaved changes.
            if (!ConfirmDiscardIfDirty()) return;

            try
            {
                IsOperationRunning = true;
                StatusText = "Recovery read...";
                _cancellationTokenSource = new CancellationTokenSource();

                PcmPackage? package = await Task.Run(() =>
                    PcmReader.RecoveryReadAsync(pcmType.Value, _cancellationTokenSource.Token));

                if (package != null)
                {
                    SetLoadedPackage(package, path: null, dirty: true);
                    StatusText = "Recovery read completed.";
                    PromptSaveAfterRead();
                }
                else
                {
                    StatusText = "Recovery read failed.";
                }
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Recovery read failed: {ex.Message}");
                StatusText = "Error occurred.";
            }
            finally
            {
                IsOperationRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }

        /// <summary>
        /// PCM Recovery -> Write. Writes the loaded document straight through the recovery path (no
        /// detection), using the PCM type the user selects. Because recovery bypasses detection, we
        /// confirm exactly which file is about to be written first.
        /// </summary>
        private async Task ExecuteRecoveryWriteAsync()
        {
            if (Vehicle == null || PcmFlasher == null || IsOperationRunning) return;

            PcmPackage? document = LoadedPackage;
            if (document == null)
            {
                const string message =
                    "No file is loaded.\n\nLoad the .phz or .bin you want to write (File -> Load File) "
                    + "before running a recovery write.";
                _logger.AddUserMessage("Recovery write: no file loaded.");
                MessageBox.Show(message, "PCM Recovery", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            PcmType? pcmType = PromptForRecoveryPcmType();
            if (pcmType == null) return;

            // Recovery skips every detection cross-check, so make sure the user knows which file this
            // is about to put on the PCM.
            string confirmation =
                $"Recovery write to a {pcmType.Value} PCM.\n\n" +
                $"File: {DocumentDisplayName()}\n" +
                $"{DescribeDocument(document)}\n\n" +
                "The PCM will NOT be detected or verified first. Write this file now?";
            if (MessageBox.Show(confirmation, "Confirm recovery write",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                _logger.AddUserMessage("Recovery write canceled by user.");
                return;
            }

            try
            {
                IsOperationRunning = true;
                StatusText = "Recovery write...";
                _cancellationTokenSource = new CancellationTokenSource();

                bool success = await Task.Run(() =>
                    PcmFlasher.RecoveryWriteAsync(document, pcmType.Value, _cancellationTokenSource.Token));

                StatusText = success ? "Recovery write completed." : "Recovery write failed.";
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage($"Recovery write failed: {ex.Message}");
                StatusText = "Error occurred.";
            }
            finally
            {
                IsOperationRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                await Task.Delay(2000);
                if (!IsOperationRunning) StatusText = "Ready";
            }
        }

        /// <summary>
        /// Ask which PCM is being recovered. Null when the user cancels. Recovery cannot auto-detect
        /// the type, so "Auto" is rejected here rather than deeper in the flow.
        /// </summary>
        private PcmType? PromptForRecoveryPcmType()
        {
            PcmTypeSelectDialogBox dialog = new() { Owner = _parentWindow };
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            if (!RecoveryMode.CanAttempt(dialog.SelectedPCMType, out string reason))
            {
                _logger.AddUserMessage(reason);
                MessageBox.Show(reason, "PCM Recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            return dialog.SelectedPCMType;
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
                bool proceedWithCancel = await PcmFlasher!.PromptForYesNo(warningMessage, "PCM Hammer");

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
            // Release the previous interface when switching to a different device (Select Device), so its
            // J2534 handle/channels don't leak for the rest of the session. A Re-Initialize reuses the
            // same Device object, so skip disposal then - otherwise we'd close the very device the new
            // Vehicle is about to wrap. Disposal needs the ShutdownSignalSource cancelled first (the Uno
            // reuse hook), same as on app shutdown.
            if (Vehicle != null && !ReferenceEquals(SelectedDevice, workingDevice))
            {
                try
                {
                    Vehicle.ShutdownSignalSource.Cancel();
                    Vehicle.Dispose();
                }
                catch (Exception exception)
                {
                    _logger.AddDebugMessage("Releasing previous device failed: " + exception.Message);
                }
            }

            SelectedDevice = workingDevice;
            Protocol protocolEngine = new();

            ToolPresentNotifier notifier = new(
                workingDevice,
                protocolEngine,
                _logger
            );

            // Empty base path: the library resolves kernels from AppContext.BaseDirectory (next to the
            // exe, where BuildAll deploys them), matching WinForms. A literal here would look in a
            // relative folder that doesn't exist and fail with "Invalid directory".
            Vehicle = new Vehicle(workingDevice, protocolEngine, _logger, notifier, string.Empty)
            {
                Enable4xReadWrite = Enable4xCom
            };

            _logger.AddDebugMessage($"Vehicle pipeline established for: {workingDevice.GetDeviceType()}");

            // Refresh your service layer with the updated Vehicle instance
            PcmFlasher = new PcmFlasher(Vehicle, _logger);
            PcmReader = new PcmReader(Vehicle, _logger);
        }

        /// <summary>
        /// This method is used to update the UI with the current status of the operation.
        /// </summary>
        private void AddInitialLogMessages()
        {
            // Version/build line comes from the shared AppInfo helper: a real "Version: x.y.z" for a
            // stamped release build, or "Build: <date time>" for an untagged dev build (the assembly
            // file version defaults to 0.0.0.0 in the csproj, and Generated.BuildTime is written at
            // compile time by the Date target). This matches WinForms and the CLI.
            string versionLine = AppInfo.GetVersionOrBuildLine(Generated.BuildTime);

            // Add user messages to the log
            _logger.AddUserMessage("PCM Hammer");
            _logger.AddUserMessage(AppInfo.CopyrightNotice);
            _logger.AddUserMessage(versionLine);
            _logger.AddUserMessage(AppInfo.GetRunningAtMessage());
            _logger.AddUserMessage("Thanks for using PCM Hammer.");
            // Add debug messages to the debug log
            _logger.AddDebugMessage("PCM Hammer");
            _logger.AddDebugMessage(AppInfo.CopyrightNotice);
            _logger.AddDebugMessage(versionLine);
            _logger.AddDebugMessage(AppInfo.GetRunningAtMessage());
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
