using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammerAvalonia.Services;
using PCMHammerAvalonia.Views;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PCMHammerAvalonia.ViewModels
{
    public partial class DevicePickerViewModel(ILogger logger, SettingsService _settingsService) : ObservableObject
    {
        private const string _prompt = "Select...";
        public Device? SelectedDevice = null;
        public event Action? RequestClose;
        public event Action? RequestAcceptAndClose;

        #region Properties
        [ObservableProperty]
        public partial string? DeviceCategory { get; set; }

        /// <summary>
        /// J2534 pass-thru devices require Windows-only vendor drivers and are not usable on Linux.
        /// </summary>
        public bool IsJ2534Supported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public string? J2534UnsupportedTooltip => IsJ2534Supported ? null : "Not supported on Linux";

        [ObservableProperty]
        public partial string? J2534DeviceType { get; set; }

        [ObservableProperty]
        public partial SerialPortInfo? SerialPort { get; set; }

        [ObservableProperty]
        public partial string? SerialPortDeviceType { get; set; }

        [ObservableProperty]
        public partial bool Enable4xReadWrite { get; set; }

        [ObservableProperty]
        public partial bool IsSerialDeviceSelected { get; set; } = true;

        partial void OnIsSerialDeviceSelectedChanged(bool value)
        {
            if (value)
            {
                DeviceCategory = "Serial";
                IsJ2534DeviceSelected = false;
            }
        }

        [ObservableProperty]
        public partial bool IsJ2534DeviceSelected { get; set; }

        partial void OnIsJ2534DeviceSelectedChanged(bool value)
        {
            if (value)
            {
                DeviceCategory = "J2534";
                IsSerialDeviceSelected = false;
            }
        }
        #endregion

        #region Commands
        [RelayCommand]
        public async Task SelectSerial()
        {
            FillSerialDeviceList();
            _ = AddDiscoveredPortsAsync();
            DeviceCategory = "Serial";
        }

        [RelayCommand]
        public async Task SelectJ2534()
        {
            _ = AddDiscoveredJ2534DevicesAsync();
            DeviceCategory = "J2534";
        }

        [RelayCommand]
        public void AutoDetect() => _ = ExecuteAutoDetect();

        [RelayCommand]
        public async Task Test() => _ = ExecuteTestSelectedDevice();

        [RelayCommand]
        public void Cancel() => RequestClose?.Invoke();

        [RelayCommand]
        public async Task Accept() => await ExecuteAcceptAndClose();
        #endregion

        // Collections for UI drop-downs
        public ObservableCollection<SerialPortInfo> SerialPorts { get; } = [];
        public ObservableCollection<string> SerialDevices { get; } = [];
        public ObservableCollection<object> J2534Devices { get; } = [];

        // Notification States
        [ObservableProperty]
        public partial string StatusText { get; set; } = "Ready.";

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        // Callbacks for View-layer UI alerts (decouples MessageBox.Show)
        public Func<string, string, Task>? ShowWarningAlertAsync { get; set; }
        public Func<string, string, Task>? ShowInfoAlertAsync { get; set; }
        public Func<string, string, Task>? ShowErrorAlertAsync { get; set; }

        public async Task ExecuteAcceptAndClose()
        {
            bool initialized = await TestSelectedDeviceAsync();
            if (initialized && SelectedDevice != null && !string.IsNullOrEmpty(DeviceCategory))
            {
                _settingsService.Settings.SavedDeviceType = DeviceCategory;
                if (DeviceCategory.Equals("Serial", StringComparison.Ordinal))
                {
                    _settingsService.Settings.SavedSerialPort = SerialPort?.PortName ?? string.Empty;
                    _settingsService.Settings.SavedSerialDevice = SerialPortDeviceType ?? string.Empty;
                    _settingsService.Settings.SavedJ2534Device = "";
                }
                else
                {
                    _settingsService.Settings.SavedSerialPort = "";
                    _settingsService.Settings.SavedSerialDevice = "";
                    _settingsService.Settings.SavedJ2534Device = J2534DeviceType;
                }
                _settingsService.Settings.SavedDevice4xCommunicationEnabled = Enable4xReadWrite;
                _settingsService.SaveSettings();
                RequestAcceptAndClose?.Invoke();
            }
            else if (string.IsNullOrEmpty(DeviceCategory))
            {
                // The only failure path TestSelectedDeviceAsync doesn't already report a specific
                // reason for; every other failure (timeout, create failed, exception, etc.) leaves
                // its own message in StatusText, which must not be clobbered here.
                StatusText = "Device test failed or invalid selection.";
            }
        }

        private async Task ExecuteTestSelectedDevice()
        {
            Device? device;
            // "target" describes the user's selection for the failure messages we can show even
            // before a device object exists (e.g. nothing matched).
            string target;
            // "onPort" is the trailing " on COMx" suffix for serial devices, empty for J2534.
            string onPort = string.Empty;
            if (DeviceCategory == DeviceConfiguration.Constants.DeviceCategorySerial)
            {
                device = DeviceFactory.CreateSerialDevice(SerialPort!.PortName, SerialPortDeviceType, logger);
                onPort = " on " + (SerialPort!.PortName ?? "(no port)");
                target = (SerialPortDeviceType ?? "serial device") + onPort;
            }
            #if !LINUX_CLI && !ANDROID
            else if (DeviceCategory == DeviceConfiguration.Constants.DeviceCategoryJ2534)
            {
                device = DeviceFactory.CreateJ2534Device(J2534DeviceType, logger);
                target = J2534DeviceType ?? "J2534 device";
            }
#endif
            else
            {
                StatusText = "No device specified.";
                await MessageBox.ShowAsync(
                    "Choose a device to test first.",
                    "Test Device",
                    MessageBoxButton.OK);
                return;
            }

            if (device == null)
            {
                StatusText = "Could not create " + target + ".";
                await MessageBox.ShowAsync(
                    "FAIL" + Environment.NewLine + Environment.NewLine +
                        "Could not create " + target + ".",
                    "Test Device",
                    MessageBoxButton.OK);
                return;
            }

            // Friendly name for the device (GetDeviceType()), plus the port it is on (serial only).
            string description = device.GetDeviceType() + onPort;

            StatusText = "Testing " + device.GetDeviceType() + "...";
            try
            {
                // Guard the test with a timeout: a defunct port (e.g. a stale Bluetooth COM
                // port) can make Initialize() hang, which would otherwise freeze the dialog.
                // The full DVI handshake (open, reset, board info, CAN check, voltage, protocol,
                // DVI setup) can legitimately take longer than a few seconds over slower USB/VM
                // passthrough, so this is generous rather than tight.
                Task<bool> initializeTask = device.Initialize();
                bool completed = await initializeTask.AwaitWithTimeout(TimeSpan.FromSeconds(20));
                if (!completed)
                {
                    StatusText = "Timed out testing " + description + ".";
                    await MessageBox.ShowAsync(
                        "FAIL" + Environment.NewLine + Environment.NewLine +
                            "Timed out trying to use " + description + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or the device may not be responding.",
                        "Test Device",
                        MessageBoxButton.OK);
                }
                else if (initializeTask.Result)
                {
                    StatusText = description + " test OK.";
                    await MessageBox.ShowAsync(
                        "OK" + Environment.NewLine + Environment.NewLine +
                            description + " initialized successfully.",
                        "Test Device",
                        MessageBoxButton.OK);
                }
                else
                {
                    StatusText = description + " test FAILED.";
                    await MessageBox.ShowAsync(
                        "FAIL" + Environment.NewLine + Environment.NewLine +
                            "Unable to initialize " + description + ".",
                        "Test Device",
                        MessageBoxButton.OK);
                }
            }
            catch (Exception exception)
            {
                StatusText = description + " test FAILED: " + exception.Message;
                await MessageBox.ShowAsync(
                    "FAIL" + Environment.NewLine + Environment.NewLine +
                        "Unable to use " + description + ":" + Environment.NewLine + Environment.NewLine + exception.Message,
                    "Test Device",
                    MessageBoxButton.OK);
            }
            finally
            {
                // Dispose is non-blocking for serial ports (see StandardPort), so this is safe
                // on the UI thread even when the underlying device is dead.
                device.Dispose();
            }
        }

        private async Task ExecuteAutoDetect()
        {
            if (SerialPort == null)
            {
                await MessageBox.ShowAsync(
                    "Choose a serial port first, then click Auto Detect to scan it.",
                    "Auto Detect",
                    MessageBoxButton.OK);
                return;
            }

            StatusText = "Scanning " + SerialPort?.PortName + " for a compatible device...";
            Device? device = null;
            try
            {
                // Bound the scan with a timeout: a defunct or busy port can make the underlying
                // open / probe sequence hang, which would otherwise freeze the dialog.
                // portName is non-null here (guarded by the IsNullOrEmpty check above).
                Task<Device?> detectTask = DeviceFactory.AutoDetectSerialDevice(SerialPort!.PortName!, logger);
                if (!await detectTask.AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                {
                    StatusText = "Auto detect timed out on " + SerialPort?.PortName + ".";
                    await MessageBox.ShowAsync(
                        "Auto detect timed out on " + SerialPort?.PortName + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or a connected device may not be responding.",
                        "Auto Detect",
                        MessageBoxButton.OK);
                    return;
                }

                device = detectTask.Result;
                if (device == null)
                {
                    StatusText = "Auto detect timed out on " + SerialPort?.PortName + ".";
                    await MessageBox.ShowAsync(
                        "Auto detect timed out on " + SerialPort?.PortName + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or a connected device may not be responding.",
                        "Auto Detect",
                        MessageBoxButton.OK);
                    return;
                }

                device = detectTask.Result;
                if (device == null)
                {
                    StatusText = "No compatible device found on " + SerialPort?.PortName + ".";
                    await MessageBox.ShowAsync(
                        "No compatible devices found on " + SerialPort?.PortName + ".",
                        "Auto Detect",
                        MessageBoxButton.OK);
                    return;
                }

                string deviceType = device.GetDeviceType();

                StatusText = "Found " + deviceType + " on " + SerialPort?.PortName + ".";
                await MessageBox.ShowAsync(
                    "Found a " + deviceType + " device on " + SerialPort?.PortName + ".",
                    "Auto Detect",
                    MessageBoxButton.OK);
            }
            catch (Exception exception)
            {
                logger.AddDebugMessage("Auto detect failed: " + exception.ToString());
                StatusText = "Auto detect failed: " + exception.Message;
                await MessageBox.ShowAsync(
                    "Auto detect failed on " + SerialPort?.PortName + ":" + Environment.NewLine + Environment.NewLine + exception.Message,
                    "Auto Detect",
                    MessageBoxButton.OK);
            }
            finally
            {
                // AutoDetectSerialDevice opens the port; dispose the device (and its port) so the
                // OK / Test path can reopen it. Dispose is non-blocking for serial ports.
                device?.Dispose();
            }
        }

        private void ExecuteSelectSerial()
        {
            FillSerialDeviceList();
            _ = AddDiscoveredPortsAsync();
            DeviceCategory = "Serial";
        }

        private void ExecuteSelectJ2534()
        {
            _ = AddDiscoveredJ2534DevicesAsync();
            DeviceCategory = "J2534";
        }

        public async Task InitializeAsync()
        {
            FillSerialDeviceList();

            // Asynchronously run background discoveries with safe timeouts
            await AddDiscoveredPortsAsync();
            await AddDiscoveredJ2534DevicesAsync();

            // Set default category based on device discovery layout
            if (SerialDevices.Count > 1) // Count includes prompt
            {
                DeviceCategory = "Serial";
            }
            else if (J2534Devices.Count > 1)
            {
                DeviceCategory = "J2534";
            }
            else
            {
                DeviceCategory = "Serial";
                StatusText = "You don't seem to have any serial ports or J2534 devices.";
            }

            // Apply persistent user configurations
            if (_settingsService.Settings.SavedDeviceType.Equals("Serial") && SerialDevices.Count > 1)
            {
                DeviceCategory = "Serial";
            }
            else if (_settingsService.Settings.SavedDeviceType.Equals("J2534") && J2534Devices.Count > 1)
            {
                DeviceCategory = "J2534";
            }

            SerialPort = SerialPorts.FirstOrDefault(p => p.PortName == _settingsService.Settings.SavedSerialPort);
            SerialPortDeviceType = _settingsService.Settings.SavedSerialDevice;
            J2534DeviceType = _settingsService.Settings.SavedJ2534Device;
            Enable4xReadWrite = _settingsService.Settings.SavedDevice4xCommunicationEnabled;
            StatusText = "Ready.";
        }

        private void FillSerialDeviceList()
        {
            SerialDevices.Add(_prompt);
            SerialDevices.Add(ElmDevice.DeviceType);
            SerialDevices.Add(AvtDevice.DeviceType);
            SerialDevices.Add(OBDXProDevice.DeviceType);
        }

        private async Task AddDiscoveredPortsAsync()
        {
            try
            {
                var ports = await Task.Run(() => PortDiscovery.GetPorts(logger).ToList());
                SerialPorts.Clear();
                foreach (var port in ports)
                    SerialPorts.Add(port);
            }
            catch (TimeoutException)
            {
                string savedPort = _settingsService.Settings.SavedSerialPort;
                StatusText = string.IsNullOrEmpty(savedPort)
                    ? "Timed out listing serial ports - a disconnected device may be stuck. Try a different port."

                        : $"Timed out listing serial ports - the saved port {savedPort} looks disconnected or stuck. Avoid it and choose a different port.";
            }
            catch (Exception ex)
            {
                logger.AddDebugMessage("Failed to list serial ports: " + ex.ToString());
                StatusText = "Unable to list serial ports: " + ex.Message;
            }
        }

        private async Task AddDiscoveredJ2534DevicesAsync()
        {
#if !LINUX_CLI && !ANDROID
            try
            {
                Task<List<J2534DotNet.J2534Device>> devicesTask = Task.Run(() => J2534DeviceFinder.FindInstalledJ2534DLLs(logger));
                if (await devicesTask.AwaitWithTimeout(TimeSpan.FromSeconds(5)))
                {
                    J2534Devices.Clear();
                    foreach (var device in devicesTask.Result)
                        J2534Devices.Add(device);
                }
                else
                {
                    string savedDevice = _settingsService.Settings.SavedJ2534Device;
                    StatusText = string.IsNullOrEmpty(savedDevice)
                        ? "Timed out listing J2534 devices - a driver may be stuck. Try a different device."
                        : $"Timed out listing J2534 devices - the saved device {savedDevice} may have a stuck driver. Avoid it and choose a different device.";
                }
            }
            catch (Exception ex)
            {
                logger.AddDebugMessage("Failed to list J2534 devices: " + ex.ToString());
                StatusText = "Unable to list J2534 devices: " + ex.Message;
            }
#else
            // J2534 pass-thru requires Windows-only vendor drivers; not available on Linux.
            J2534Devices.Clear();
            await Task.CompletedTask;
#endif
        }

        public async Task AutoDetectSerialAsync()
        {
            if (SerialPort == null)
            {
                if (ShowInfoAlertAsync != null)
                {
                    await ShowInfoAlertAsync("Choose a serial port first, then click Auto Detect to scan it.", "Auto Detect");
                }
                return;
            }

            IsBusy = true;
            StatusText = $"Scanning {SerialPort} for a compatible device...";
            Device? device = null;

            try
            {
                Task<Device?> detectTask = DeviceFactory.AutoDetectSerialDevice(SerialPort!.PortName!, logger);
                if (!await detectTask.AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                {
                    StatusText = $"Auto detect timed out on {SerialPort}.";
                    if (ShowWarningAlertAsync != null)
                    {
                        await ShowWarningAlertAsync($"Auto detect timed out on {SerialPort}.{Environment.NewLine}{Environment.NewLine}The port may be in use, or a connected device may not be responding.", "Auto Detect");
                    }
                    return;
                }

                device = detectTask.Result;
                if (device == null)
                {
                    StatusText = $"No compatible device found on {SerialPort}.";
                    if (ShowInfoAlertAsync != null)
                    {
                        await ShowInfoAlertAsync($"No compatible devices found on {SerialPort}.", "Auto Detect");
                    }
                    return;
                }

                string detectedType = device.GetDeviceType();
                DeviceCategory = DeviceConfiguration.Constants.DeviceCategorySerial;
                SerialPortDeviceType = detectedType;

                StatusText = $"Found {detectedType} on {SerialPort}.";
                if (ShowInfoAlertAsync != null)
                {
                    await ShowInfoAlertAsync($"Found a {detectedType} device on {SerialPort}.", "Auto Detect");
                }
            }
            catch (Exception ex)
            {
                logger.AddDebugMessage("Auto detect failed: " + ex.ToString());
                StatusText = "Auto detect failed: " + ex.Message;
                if (ShowErrorAlertAsync != null)
                {
                    await ShowErrorAlertAsync($"Auto detect failed on {SerialPort}:{Environment.NewLine}{Environment.NewLine}{ex.Message}", "Auto Detect");
                }
            }
            finally
            {
                device?.Dispose();
                IsBusy = false;
            }
        }

        public async Task<bool> TestSelectedDeviceAsync()
        {
            Device? device = null;
            string target;
            string onPort = string.Empty;

            if (SerialPort == null)
            {
                StatusText = "Choose a serial port first.";
                return false;
            }

            var match = SerialPortRegex().Match(SerialPort!.PortName!);
            if (match.Success && SerialPort.PortName!.Length > 4)
            {
                string cleanedName = match.Groups[0].Value;
                SerialPort = SerialPorts.FirstOrDefault(p => p.PortName == cleanedName);
            }

            if (IsSerialDeviceSelected)
            {
                device = DeviceFactory.CreateSerialDevice(SerialPort!.PortName!, SerialPortDeviceType, logger);
                onPort = " on " + (SerialPort!.PortName! ?? "(no port)");
                target = (SerialPortDeviceType ?? "serial device") + onPort;
            }
            #if !LINUX_CLI && !ANDROID
            else if (IsJ2534DeviceSelected)
            {
                device = DeviceFactory.CreateJ2534Device(J2534DeviceType, logger);
                target = J2534DeviceType ?? "J2534 device";
            }
#endif
            else
            {
                StatusText = "No device specified.";
                if (ShowInfoAlertAsync != null)
                {
                    await ShowInfoAlertAsync("Choose a device to test first.", "Test Device");
                }
                return false;
            }

            if (device == null)
            {
                StatusText = $"Could not create {target}.";
                if (ShowErrorAlertAsync != null)
                {
                    await ShowErrorAlertAsync($"FAIL{Environment.NewLine}{Environment.NewLine}Could not create {target}.", "Test Device");
                }
                return false;
            }

            string description = device.GetDeviceType() + onPort;
            IsBusy = true;
            StatusText = $"Testing {device.GetDeviceType()}...";
            bool success = false;

            try
            {
                // See the comment in ExecuteTestSelectedDevice: a full DVI handshake over
                // USB/VM passthrough can legitimately take longer than a few seconds.
                Task<bool> initializeTask = device.Initialize();
                bool completed = await initializeTask.AwaitWithTimeout(TimeSpan.FromSeconds(20));
                if (!completed)
                {
                    StatusText = $"Timed out testing {description}.";
                    if (ShowWarningAlertAsync != null)
                    {
                        await ShowWarningAlertAsync($"FAIL{Environment.NewLine}{Environment.NewLine}Timed out trying to use {description}.{Environment.NewLine}{Environment.NewLine}The port may be in use, or the device may not be responding.", "Test Device");
                    }
                }
                else if (initializeTask.Result)
                {
                    StatusText = $"{description} test OK.";
                    success = true;
                    SelectedDevice = device;

                    if (ShowInfoAlertAsync != null)
                    {
                        await ShowInfoAlertAsync($"OK{Environment.NewLine}{Environment.NewLine}{description} initialized successfully.", "Test Device");
                    }
                }
                else
                {
                    StatusText = $"{description} test FAILED.";
                    if (ShowErrorAlertAsync != null)
                    {
                        await ShowErrorAlertAsync($"FAIL{Environment.NewLine}{Environment.NewLine}Unable to initialize {description}.", "Test Device");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"{description} test FAILED: {ex.Message}";
                if (ShowErrorAlertAsync != null)
                {
                    await ShowErrorAlertAsync($"FAIL{Environment.NewLine}{Environment.NewLine}Unable to use {description}:{Environment.NewLine}{Environment.NewLine}{ex.Message}", "Test Device");
                }
            }
            finally
            {
                IsBusy = false;
                // Only a successfully initialized device is kept open; any other outcome
                // (timeout, failure, exception) must dispose the device/port here, since
                // nothing else owns it in that case.
                if (!success)
                {
                    device.Dispose();
                }
            }

            return success;
        }

        [GeneratedRegex(@"COM\d+")]
        private static partial Regex SerialPortRegex();
    }
}