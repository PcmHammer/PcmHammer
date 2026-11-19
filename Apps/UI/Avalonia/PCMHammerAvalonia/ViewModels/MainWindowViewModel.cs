using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammerAvalonia.Helpers;
using PCMHammerAvalonia.Services;
using PCMHammerAvalonia.Views;
using PCMHammerAvalonia.Views.DialogBoxes;
using Application = Avalonia.Application;

namespace PCMHammerAvalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    #region Fields
    private readonly MainWindowLogger _logger;
    private readonly FileDialogService _fileDialogService;
    private readonly Window _parentWindow;
    private CancellationTokenSource? _cancellationTokenSource;

    // A kernel operation's exit reboots the PCM; an unlock within ~10s of that reboot lands in the PCM's
    // power-on security window and the kernel upload is refused (7F 34), or the unlock reply is odd/short.
    // Track when the last operation finished so the next kernel op waits out the remainder of that settle
    // time (ported from the Linux CLI's 86147d78 "Wait 10 seconds before kernel operations").
    private DateTime _lastKernelOperationUtc = DateTime.MinValue;
    private const int PcmReadySeconds = 10;
    private bool CanReInitialize() => SelectedDevice is not null;
    private bool CanExecuteSelectedDevice() => SelectedDevice is not null;
    private readonly SettingsService _settingsService;
    #endregion

    #region Properties
    [ObservableProperty]
    public partial PcmFlasher? PcmFlasher { get; set; }

    [ObservableProperty]
    public partial PcmReader? PcmReader { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReInitializeDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadPCMCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyPCMCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeVINCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteParametersCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteOSCalibrationBootCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteFullFlashCloneCommand))]
    [NotifyCanExecuteChangedFor(nameof(BruteForceUnlockCommand))]
    [NotifyCanExecuteChangedFor(nameof(HaltRunningKernelCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCrankRelearnCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadPropertiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(WritePCMCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestWriteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCurrentCommand))]
    public partial Device? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial string LogText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DebugLogText { get; set; } = string.Empty;

    // Bound two-way to the mobile DrawerPage.IsOpen: the hamburger toggles it, and the sidebar commands
    // set it false so the drawer closes when an action is chosen (desktop ignores it).
    [ObservableProperty]
    public partial bool IsDrawerOpen { get; set; }

    [ObservableProperty]
    public partial bool IsResultsCopiedFeedbackVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDebugCopiedFeedbackVisible { get; set; }

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
    public partial Vehicle? Vehicle { get; set; }

    [ObservableProperty]
    public partial bool IsOperationRunning { get; set; }

    #endregion

    #region Commands
    [RelayCommand]
    public async Task CopyLog(string logText)
    {
        if (string.IsNullOrEmpty(logText)) return;

        // Get active desktop lifetime to access the main window's clipboard
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(logText);
            }
        }

        // Trigger visual feedback
        if (logText == LogText)
            IsResultsCopiedFeedbackVisible = true;
        else if (logText == DebugLogText)
            IsDebugCopiedFeedbackVisible = true;

        // Wait 1 second, then hide the feedback
        await Task.Delay(1000);

        if (logText == LogText)
            IsResultsCopiedFeedbackVisible = false;
        else if (logText == DebugLogText)
            IsDebugCopiedFeedbackVisible = false;
    }

    #region Commands (File Menu)
    [RelayCommand]
    public async Task SaveResultsLog() => await SaveLogFileAsync("UserLog", LogText);

    [RelayCommand]
    public async Task SaveDebugLog() => await SaveLogFileAsync("DebugLog", DebugLogText);

    [RelayCommand]
    public static void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
    #endregion

    #region Commands (Tools Menu & Operations)
    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task ReadPCM()
    {
        IsDrawerOpen = false;
        await ExecuteReadPCMAsync(true, PcmType.Undefined);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task VerifyPCM()
    {
        IsDrawerOpen = false;
        await ExecuteVerificationAsync();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task ChangeVIN() => await ExecuteChangeVINAsync();

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task WriteParameters() => await ExecuteWritePCMAsync(WriteType.Parameters);

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task WriteOSCalibrationBoot() => await ExecuteWritePCMAsync(WriteType.OsPlusCalibrationPlusBoot);

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task WriteFullFlashClone() => await ExecuteWritePCMAsync(WriteType.Full);

    [RelayCommand]
    public async Task TestFileChecksums() => await TestFileChecksumsAsync();

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task BruteForceUnlock()
    {
        if (Vehicle == null) return;

        StatusText = "Brute Force Unlocking...";

        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            owner = desktop.MainWindow;

        var dialog = new BruteForceDialogBox(vehicle: Vehicle, logger: _logger);

        // Pass 'this' (the current parent window) to make it modal
        bool? result = await dialog.ShowDialog<bool?>(owner!);
        StatusText = result == true ? "Brute Force Unlock Completed." : "Ready";
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
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

    /// <summary>
    /// Opens Crank Relearn (ports the Uno Crank Relearn page). It reads the entry conditions from the
    /// PCM and, only when the user presses Start, sends the relearn request. It does not touch flash.
    /// The window is modal, so no other operation can use the device while it is open.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task OpenCrankRelearn()
    {
        if (Vehicle == null) return;
        if (IsOperationRunning) return;

        IsDrawerOpen = false;
        StatusText = "Crank Relearn";

        try
        {
            IsOperationRunning = true;
#if ANDROID
            var viewModel = new CrankRelearnViewModel(Vehicle, _logger);
            var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
            {
                Title = "Crank Relearn",
                Content = new Views.DialogBoxes.CrankRelearnView { DataContext = viewModel },
                CloseButtonText = "Close",
            };

            TopLevel? topLevel = Views.MainView.Current is { } view ? TopLevel.GetTopLevel(view) : null;
            if (topLevel != null) await dialog.ShowAsync(topLevel);
            else await dialog.ShowAsync();

            // Closing the dialog must stop polling the PCM so the device is released.
            viewModel.Stop();
#else
            Window? owner = null;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                owner = desktop.MainWindow;

            var dialog = new CrankRelearnDialogBox(Vehicle, _logger);
            await dialog.ShowDialog<bool?>(owner!);
#endif
        }
        finally
        {
            IsOperationRunning = false;
            StatusText = "Ready";
        }
    }
    #endregion

    #region Commands (Options Menu)
    [RelayCommand]
    public async Task UserDefinedKey()
    {
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            owner = desktop.MainWindow;

        var dialog = new UserDefinedKeyDialogBox();

        // Pass 'this' (the current parent window) to make it modal
        bool? result = await dialog.ShowDialog<bool?>(owner!);
        StatusText = result == true ? "User-defined key set." : "Ready";
    }

    [RelayCommand]
    public async Task Settings()
    {
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            owner = desktop.MainWindow;

        var dialog = new SettingsWindow();

        // Pass 'this' (the current parent window) to make it modal
        bool? result = await dialog.ShowDialog<bool?>(owner!);
        StatusText = result == true ? "Settings updated." : "Ready";
    }
    #endregion

    #region Commands (Device & Operations Sidebar)
    [RelayCommand]
    public async Task SelectDevice()
    {
        IsDrawerOpen = false;
        try
        {
#if ANDROID
            // Android is single-view: there is no Window to own a modal dialog, so host the picker
            // content in a ContentDialog overlay on the mobile view's TopLevel instead.
            await SelectDeviceAndroidAsync();
            return;
#else
            Window? owner = null;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                owner = desktop.MainWindow;

            var dialog = new DevicePickerDialogBox(_logger, _settingsService);

            StatusText = "Selecting Device...";

            // Pass 'this' (the current parent window) to make it modal
            bool? result = await dialog.ShowDialog<bool?>(owner!);

            if (result == true)
            {
                Device? workingDevice = dialog.SelectedDevice;
                bool enable4xReadWrite = dialog.Enable4xReadWrite;

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
            else
            {
                StatusText = "Ready";
            }
#endif
        }
        catch (Exception ex)
        {
            _logger.AddDebugMessage($"SelectDevice failed: {ex}");
            StatusText = $"Error selecting device: {ex.Message}";
        }
    }

#if ANDROID
    // Mobile device picker: the same DevicePickerViewModel + DevicePickerView content the desktop
    // Window hosts, shown in a FluentAvalonia ContentDialog over the view's TopLevel. The view model's
    // RequestAcceptAndClose / RequestClose events close the overlay; on accept its SelectedDevice is used.
    private async Task SelectDeviceAndroidAsync()
    {
        var pickerViewModel = new DevicePickerViewModel(_logger, _settingsService);
        var content = new Views.DialogBoxes.DevicePickerView { DataContext = pickerViewModel };
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = "Select Device",
            Content = content,
        };

        bool accepted = false;
        pickerViewModel.RequestClose += () => dialog.Hide();
        pickerViewModel.RequestAcceptAndClose += () =>
        {
            accepted = true;
            dialog.Hide();
        };

        StatusText = "Selecting Device...";

        // Discover devices (paired Bluetooth adapters) once the dialog is up.
        _ = pickerViewModel.InitializeAsync();

        TopLevel? topLevel = Views.MainView.Current is { } view ? TopLevel.GetTopLevel(view) : null;
        if (topLevel != null)
        {
            await dialog.ShowAsync(topLevel);
        }
        else
        {
            await dialog.ShowAsync();
        }

        if (accepted && pickerViewModel.SelectedDevice != null)
        {
            InitializeDeviceAndVehicle(pickerViewModel.SelectedDevice, pickerViewModel.Enable4xReadWrite);
        }

        StatusText = "Ready";
    }
#endif

    [RelayCommand(CanExecute = nameof(CanReInitialize))]
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

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task ReadProperties()
    {
        IsDrawerOpen = false;
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

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task WritePCM()
    {
        IsDrawerOpen = false;
        await ExecuteWritePCMAsyncWithDialog();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task TestWrite()
    {
        IsDrawerOpen = false;
        await ExecuteWritePCMAsync(WriteType.TestWrite);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSelectedDevice))]
    public async Task CancelCurrent()
    {
        if (IsOperationRunning)
        {
            string warningMessage = "Canceling now could leave your PCM in an unbootable state (bricked)." + Environment.NewLine +
                                "Are you absolutely sure you want to take that risk?";
            bool proceedWithCancel = await PcmFlasher.PromptForYesNo("PCM Hammer", warningMessage);

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
        _logger = new MainWindowLogger(this);
        _settingsService = new SettingsService();
        _fileDialogService = new FileDialogService(_settingsService);
        _logger.ProgressBarUpdated += (percent, visible) =>
        {
            // No need to hop onto the UI thread to update properties, we're already there.
            ProgressPercent = percent * 100;
        };
        AddInitialLogMessages();

        // Load up saved device and vehicle information from the settings file if configured to do so

        // Read properties exactly as before:
        bool retainConfigOnExit = _settingsService.Settings.RetainDeviceConfigurationOnExit;
        string savedType = _settingsService.Settings.SavedDeviceType;

        // Updating and persisting changes:
        _settingsService.Settings.SavedDeviceType = "AVT_852";
        _settingsService.SaveSettings();

        if (retainConfigOnExit && !string.IsNullOrEmpty(savedType))
            _ = TrySilentDeviceConnectionAsync();
    }

    /// <summary>
    /// Parameterless constructor used by the Avalonia XAML Designer and by the mobile (single-view)
    /// host, which has no owning Window. It wires the logger and services the same way as the desktop
    /// constructor (so Results/Debug output flows), minus the Window-dependent startup.
    /// </summary>
    public MainWindowViewModel()
    {
        _parentWindow = null!;
        _logger = new MainWindowLogger(this);
        _settingsService = new SettingsService();
        _fileDialogService = new FileDialogService(_settingsService);
        _logger.ProgressBarUpdated += (percent, visible) =>
        {
            ProgressPercent = percent * 100;
        };
        AddInitialLogMessages();
    }

    public async Task HandleApplicationShutdownAsync()
    {
        var tasks = new List<Task>();

        if (_settingsService.Settings.SaveResultsLogOnExit && SaveResultsLogCommand.CanExecute(null))
            tasks.Add(SaveLogFileAsync("UserLog", LogText));

        if (_settingsService.Settings.SaveDebugLogOnExit && SaveDebugLogCommand.CanExecute(null))
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
    }

    #region Command Execution Methods
    private async Task ExecuteReadPCMAsync(bool useAutoPcmType, PcmType selectedPcmType)
    {
        if (Vehicle == null || PcmReader == null || IsOperationRunning)
            return;

        StatusText = "Preparing for Read...";

#if ANDROID
        // On Android the save picker hands back a content:// destination with no local file path, so
        // read into a private temp file first and then stream it into the user's chosen location.
        // Suggest the name without an extension: Android's save dialog appends the picker's default
        // extension itself, so including ".bin" here would produce a doubled "...bin.bin".
        Avalonia.Platform.Storage.IStorageFile? destination =
            await _fileDialogService.PickBinSaveFileAsync($"PCM_Read_{DateTime.Now:yyyyMMdd_HHmmss}");

        if (destination == null)
        {
            _logger.AddUserMessage("Read operation canceled by user (no file selected).");
            StatusText = "Ready";
            return;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"pcm_read_{Guid.NewGuid():N}.bin");
        try
        {
            bool success = await RunReadAsync(tempPath, useAutoPcmType, selectedPcmType);

            if (success && File.Exists(tempPath))
            {
                StatusText = "Saving file...";
                await using var source = File.OpenRead(tempPath);
                await using var target = await destination.OpenWriteAsync();
                await source.CopyToAsync(target);
                _logger.AddUserMessage($"Saved read to: {destination.Name}");
                StatusText = "Read Completed Successfully!";
                await Task.Delay(2000);
                StatusText = "Ready";
            }
        }
        catch (Exception ex)
        {
            _logger.AddUserMessage($"Failed to save read file: {ex.Message}");
            StatusText = "Error occurred.";
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort cleanup */ }
        }
#else
        // Await the dialog directly on the UI thread to prevent deadlocks
        string? selectedFilePath = await _fileDialogService.SaveBinFileDialog();

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            _logger.AddUserMessage("Read operation canceled by user (no file selected).");
            StatusText = "Ready";
            return;
        }

        await RunReadAsync(selectedFilePath, useAutoPcmType, selectedPcmType);
#endif
    }

    // Core read loop shared by desktop (writes straight to the chosen path) and Android (writes to a
    // temp path that is then streamed into the content:// destination). Returns whether the read
    // completed successfully.
    // Waits out the remainder of the PCM settle time since the last kernel operation. The previous op's
    // kernel exit reboots the PCM; starting the next op too soon gets a refused kernel upload (7F 34). A
    // first op, or one after the PCM has sat past the window, does not wait. Proven on a bench P01: gaps
    // of 10s+ always worked, ~2s was refused (ported from the Linux CLI's 86147d78).
    private async Task WaitForPcmReadyAsync()
    {
        double remaining = PcmReadySeconds - (DateTime.UtcNow - _lastKernelOperationUtc).TotalSeconds;
        if (remaining <= 0)
        {
            return;
        }

        _logger.AddUserMessage($"Waiting {Math.Ceiling(remaining):0} seconds for the PCM to be ready after the previous operation...");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(remaining), _cancellationTokenSource?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> RunReadAsync(string path, bool useAutoPcmType, PcmType selectedPcmType)
    {
        bool success = false;
        try
        {
            IsOperationRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            await WaitForPcmReadyAsync();

            StatusText = "Reading PCM Contents...";

            // Await the async reader directly without wrapping in Task.Run
            success = await PcmReader!.ReadPcmAsync(
                path,
                useAutoPcmType,
                selectedPcmType,
                _cancellationTokenSource.Token);

            StatusText = success ? "Read Completed Successfully!" : "Read Failed.";
        }
        catch (OperationCanceledException)
        {
            _logger.AddUserMessage("Read operation canceled by user.");
            StatusText = "Canceled";
        }
        catch (Exception ex)
        {
            _logger.AddUserMessage($"Critical error during read: {ex.Message}");
            StatusText = "Error occurred.";
        }
        finally
        {
            _lastKernelOperationUtc = DateTime.UtcNow;
            IsOperationRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            // Visual delay before resetting status text
            await Task.Delay(2000);
            if (StatusText is "Read Completed Successfully!" or "Read Failed." or "Canceled" or "Error occurred.")
            {
                StatusText = "Ready";
            }
        }
        return success;
    }
    private async Task ExecuteReadPCMAsyncWithDialog()
    {
        var dialog = new WriteOperationDialogBox();
        var viewModel = new WriteTypeViewModel()
        {
            SelectedWriteType = WriteType.Full,
            SelectedPCMType = PcmType.Undefined
        };

        dialog.DataContext = viewModel;

        // Resolve the active desktop main window as the owner
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window? parentWindow = desktop.MainWindow;

            if (parentWindow != null)
            {
                bool? result = await dialog.ShowDialog<bool?>(parentWindow);

                if (result == true)
                {
                    bool useAutoPcmType = viewModel.SelectedPCMType == PcmType.Undefined;
                    await ExecuteReadPCMAsync(useAutoPcmType, viewModel.SelectedPCMType);
                }
            }
        }
    }
    private async Task ExecuteVerificationAsync()
    {
        if (Vehicle == null || PcmFlasher == null || IsOperationRunning)
            return;

        // Resolve owner window for modal presentation
        Window? activeWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (activeWindow == null)
        {
            _logger.AddUserMessage("Verification error: Unable to locate main window host.");
            return;
        }

        PcmTypeSelectDialogBox dialog = new();
        bool? dialogResult = await dialog.ShowDialog<bool?>(activeWindow);

        if (dialogResult != true)
        {
            _logger.AddUserMessage("Comparison canceled by user (dialog closed).");
            StatusText = "Ready";
            return;
        }

        bool useAutoPcmType = dialog.SelectedPCMType == PcmType.Undefined;
        StatusText = "Preparing for Comparison...";

        // FIX: Await the async file picker call
        string? selectedFilePath = await _fileDialogService.OpenBinFileDialog();

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            _logger.AddUserMessage("Comparison canceled.");
            StatusText = "Ready";
            return;
        }

        try
        {
            IsOperationRunning = true;
            StatusText = "Comparing PCM Blocks...";
            _cancellationTokenSource = new CancellationTokenSource();

            PcmType forcedPcmType = useAutoPcmType ? PcmType.Undefined : dialog.SelectedPCMType;

            bool success = await PcmFlasher.WritePcmAsync(
                WriteType.Compare,
                selectedFilePath,
                useAutoPcmType,
                forcedPcmType,
                suppressOSIDWarning: false,
                _cancellationTokenSource.Token);

            StatusText = success ? "Comparison Complete!" : "Comparison Found Differences or Failed.";
        }
        catch (OperationCanceledException)
        {
            _logger.AddUserMessage("Comparison operation canceled by user.");
            StatusText = "Canceled";
        }
        catch (Exception ex)
        {
            _logger.AddUserMessage($"Critical error during verification: {ex.Message}");
            StatusText = "Error occurred.";
        }
        finally
        {
            IsOperationRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            await Task.Delay(2000);
            if (StatusText is "Comparison Complete!" or "Comparison Found Differences or Failed." or "Canceled" or "Error occurred.")
            {
                StatusText = "Ready";
            }
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

            // Resolve the active window host for modals
            Window? ownerWindow = _parentWindow;
            if (ownerWindow == null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                ownerWindow = desktop.MainWindow;
            }

            if (ownerWindow == null)
            {
                _logger.AddUserMessage("VIN change error: Unable to locate parent window host.");
                return;
            }

            var vinViewModel = new ChangeVinViewModel(vinResponse.Value);
            var vinDialog = new ChangeVinDialogBox(vinViewModel);

            // ShowDialog is asynchronous in Avalonia
            bool? dialogResult = await vinDialog.ShowDialog<bool?>(ownerWindow);

            if (dialogResult == true)
            {
                string cleanVin = vinViewModel.Vin.Trim();
                _logger.AddUserMessage($"Attempting to write updated VIN: {cleanVin}");

                bool unlocked = await Vehicle.UnlockEcu(info.KeyAlgorithm, new());
                if (!unlocked)
                {
                    _logger.AddUserMessage("Unable to unlock PCM. Authorization Denied.");
                    await MessageBox.ShowAsync("Unable to unlock PCM. Operation aborted.", "Error", MessageBoxButton.OK);
                    return;
                }

                Response<bool> vinModified = await Vehicle.UpdateVin(cleanVin);
                if (vinModified.Value)
                {
                    _logger.AddUserMessage($"VIN successfully updated to: {cleanVin}");
                    await MessageBox.ShowAsync($"VIN updated to {cleanVin} successfully.", "Success", MessageBoxButton.OK);
                }
                else
                {
                    _logger.AddUserMessage($"Failed to commit changes. Error code: {vinModified.Status}");
                    await MessageBox.ShowAsync($"Unable to change the VIN. Error: {vinModified.Status}", "Operation Failed", MessageBoxButton.OK);
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
        if (IsOperationRunning) return;

        StatusText = "Selecting file for validation...";

        // FIX 1: Await the file picker task cleanly
        string? selectedFilePath = await _fileDialogService.OpenBinFileDialog();

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            StatusText = "Ready";
            return;
        }

        _logger.AddUserMessage($"Examining {selectedFilePath}");
        StatusText = "Validating binary checksums...";

        try
        {
            IsOperationRunning = true;

            // FIX 2: Load binary bytes asynchronously without manual stream allocations
            byte[] image = await File.ReadAllBytesAsync(selectedFilePath);

            // FIX 3: Offload CPU-bound validation to the thread pool with synchronous execution
            string validationResult = await Task.Run(() =>
            {
                FileValidator validator = new(image, _logger);

                if (validator.IdentifyAndValidate())
                {
                    string pcmDescription = new OSIDInfo(validator.GetFileType()).Description;
                    return $"File is {pcmDescription}.\r\nAll checksums are valid.";
                }

                return "This file is corrupt or its format is unknown to PCMHammer. It would render your PCM unusable.";
            });

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

            await Task.Delay(2000);
            if (StatusText is "Validation Complete." or "Error verifying file.")
            {
                StatusText = "Ready";
            }
        }
    }
    private async Task<bool> TrySilentDeviceConnectionAsync()
    {
        // Double check safety guards
        if (!_settingsService.Settings.RetainDeviceConfigurationOnExit ||
            string.IsNullOrEmpty(_settingsService.Settings.SavedDeviceType))
        {
            return false;
        }

        try
        {
            StatusText = "Restoring saved device configuration...";

            var backgroundViewModel = new DevicePickerViewModel(_logger, _settingsService)
            {
                DeviceCategory = _settingsService.Settings.SavedDeviceType
            };

            if (backgroundViewModel.DeviceCategory.Equals("Serial", StringComparison.Ordinal))
            {
                backgroundViewModel.SerialPort = backgroundViewModel.SerialPorts.FirstOrDefault(p => p.PortName == _settingsService.Settings.SavedSerialPort);
                backgroundViewModel.SerialPortDeviceType = _settingsService.Settings.SavedSerialDevice;
            }
            else
            {
                backgroundViewModel.J2534DeviceType = _settingsService.Settings.SavedJ2534Device;
            }

            backgroundViewModel.Enable4xReadWrite = _settingsService.Settings.SavedDevice4xCommunicationEnabled;

            await backgroundViewModel.TestSelectedDeviceAsync();

            if (backgroundViewModel.SelectedDevice != null)
            {
                InitializeDeviceAndVehicle(
                    backgroundViewModel.SelectedDevice,
                    backgroundViewModel.Enable4xReadWrite
                );
                StatusText = "Ready";
                backgroundViewModel.AcceptCommand.Execute(null);
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
    private async Task ExecuteWritePCMAsync(WriteType writeType, PcmType pcmType = PcmType.Undefined, bool suppressOSIDWarning = false)
    {
        if (Vehicle == null || PcmFlasher == null || IsOperationRunning)
            return;

#if ANDROID
        // Single-view: no Window to own the "powered 10s?" DelayDialog, so show the same warning as a
        // ContentDialog confirm on the view's TopLevel instead.
        bool proceed = await ConfirmFlashDelayAndroidAsync();
        if (!proceed)
        {
            _logger.AddUserMessage("Flash operation canceled by user.");
            StatusText = "Ready";
            return;
        }
#else
        Window? owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner == null)
        {
            _logger.AddUserMessage("Write error: Unable to locate main window host.");
            return;
        }

        var dialog = new DelayDialogBox();
        bool? result = await dialog.ShowDialog<bool?>(owner);

        if (result != true)
        {
            _logger.AddUserMessage("Flash operation canceled by user.");
            StatusText = "Ready";
            return;
        }
#endif

        // FIX 1: Await the file dialog picker
        string? selectedFilePath = await _fileDialogService.OpenBinFileDialog();

        if (string.IsNullOrWhiteSpace(selectedFilePath))
        {
            _logger.AddUserMessage("Flash operation canceled by user (no file selected).");
            StatusText = "Ready";
            return;
        }

        try
        {
            IsOperationRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            await WaitForPcmReadyAsync();

            StatusText = "Writing to PCM...";

            bool useAuto = pcmType == PcmType.Undefined;

            // FIX 2: Await WritePcmAsync directly without Task.Run wrapping
            bool success = await PcmFlasher.WritePcmAsync(
                writeType,
                selectedFilePath,
                useAutoPcmType: useAuto,
                selectedPcmType: pcmType,
                cancellationToken: _cancellationTokenSource.Token,
                suppressOSIDWarning: suppressOSIDWarning);

            StatusText = success ? "Write Operation Completed." : "Write Operation Failed.";
        }
        catch (OperationCanceledException)
        {
            _logger.AddUserMessage("Write operation canceled by user.");
            StatusText = "Canceled";
        }
        catch (Exception ex)
        {
            _logger.AddUserMessage($"Critical error: {ex.Message}");
            StatusText = "Error occurred.";
        }
        finally
        {
            _lastKernelOperationUtc = DateTime.UtcNow;
            IsOperationRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            await Task.Delay(2000);
            if (StatusText is "Write Operation Completed." or "Write Operation Failed." or "Canceled" or "Error occurred.")
            {
                StatusText = "Ready";
            }
        }
    }
    private async Task ExecuteWritePCMAsyncWithDialog()
    {
#if ANDROID
        await ExecuteWritePCMAndroidWithDialogAsync();
#else
        Window? owner = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            owner = desktop.MainWindow;

        var dialog = new WriteOperationDialogBox();

        // Pass 'this' (the current parent window) to make it modal
        bool? result = await dialog.ShowDialog<bool?>(owner!);

        if (result == true)
        {
            await ExecuteWritePCMAsync(dialog.SelectedWriteType, dialog.SelectedPCMType, dialog.SuppressOSIDWarning);
        }
#endif
    }

#if ANDROID
    // Mobile write-type picker: the same WriteTypeViewModel + WriteOperationView content the desktop
    // Window hosts, shown in a FluentAvalonia ContentDialog over the view's TopLevel.
    private async Task ExecuteWritePCMAndroidWithDialogAsync()
    {
        var pickerViewModel = new WriteTypeViewModel();
        pickerViewModel.SelectedPCMType = pickerViewModel.PCMTypes[0];
        pickerViewModel.SelectedWriteType = pickerViewModel.WriteTypes[0];

        var content = new Views.DialogBoxes.WriteOperationView { DataContext = pickerViewModel };
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = "Write Type",
            Content = content,
        };

        bool accepted = false;
        pickerViewModel.RequestClose += () => dialog.Hide();
        pickerViewModel.RequestAcceptandClose += () =>
        {
            accepted = true;
            dialog.Hide();
        };

        TopLevel? topLevel = Views.MainView.Current is { } view ? TopLevel.GetTopLevel(view) : null;
        if (topLevel != null)
        {
            await dialog.ShowAsync(topLevel);
        }
        else
        {
            await dialog.ShowAsync();
        }

        if (accepted)
        {
            await ExecuteWritePCMAsync(
                pickerViewModel.SelectedWriteType,
                pickerViewModel.SelectedPCMType,
                pickerViewModel.SuppressOSIDWarning);
        }
    }

    // The "has the PCM been powered on for 10 seconds?" safety confirm, shown as a ContentDialog on
    // single-view (Android) where the DelayDialog Window cannot be owned. Returns true to proceed.
    private static async Task<bool> ConfirmFlashDelayAndroidAsync()
    {
        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = "Ready to flash?",
            Content =
                "Has the PCM been powered on for at least 10 seconds?\n\n" +
                "If not, please wait for it to initialize before continuing. If you skip this step, the " +
                "write process may be interrupted, and there's a good chance you will need to temporarily " +
                "remove the PCM's fuse to get it working properly again.",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
        };

        TopLevel? topLevel = Views.MainView.Current is { } view ? TopLevel.GetTopLevel(view) : null;
        FluentAvalonia.UI.Controls.FAContentDialogResult result = topLevel != null
            ? await dialog.ShowAsync(topLevel)
            : await dialog.ShowAsync();

        return result == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary;
    }
#endif
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

        // Base path for kernels. On desktop this is empty, so the library resolves them from
        // AppContext.BaseDirectory (next to the exe, where BuildAll deploys them), matching WinForms/WPF.
        // On Android there is no exe directory, so the head copies the bundled kernels into the app's
        // files dir at startup and records it in PlatformPaths.KernelDirectory; pass that here.
        string kernelBasePath = PlatformPaths.KernelDirectory ?? string.Empty;
        Vehicle = new Vehicle(workingDevice, protocolEngine, _logger, notifier, kernelBasePath)
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
            string? destinationPath;

            if (_settingsService.Settings.UseLogSaveAsDialog)
            {
                // FIX: Await the file picker task
                destinationPath = await _fileDialogService.GetLogSavePath(defaultName);
            }
            else
            {
                string logDir = _settingsService.Settings.LogDirectory;

                // Guard against missing directory when saving automatically
                if (!string.IsNullOrWhiteSpace(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                destinationPath = Path.Combine(logDir, defaultName);
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                _logger.AddUserMessage($"{logName} save operation canceled.");
                return;
            }

            // Write log contents asynchronously
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
