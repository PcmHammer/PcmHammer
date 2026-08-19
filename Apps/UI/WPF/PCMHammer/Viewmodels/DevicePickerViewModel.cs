using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCMHammer.Viewmodels
{
    public partial class DevicePickerViewModel(ILogger logger) : ObservableObject
    {
        private const string _prompt = "Select...";
        public Device? SelectedDevice = null;
        public event Action? RequestClose;
        public event Action? RequestAcceptAndClose;

        #region Properties
        [ObservableProperty]
        public partial string? DeviceCategory { get; set; }

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
        public string StatusText { get; set; } = "Ready.";
        public bool IsBusy { get; set; }

        // Callbacks for View-layer UI alerts (decouples MessageBox.Show)
        public Func<string, string, Task>? ShowWarningAlertAsync { get; set; }
        public Func<string, string, Task>? ShowInfoAlertAsync { get; set; }
        public Func<string, string, Task>? ShowErrorAlertAsync { get; set; }

        public async Task ExecuteAcceptAndClose()
        {
            await TestSelectedDeviceAsync();
            if (SelectedDevice != null && !string.IsNullOrEmpty(DeviceCategory))
            {
                Properties.Settings.Default.SavedDeviceType = DeviceCategory;
                if (DeviceCategory.Equals("Serial", StringComparison.Ordinal))
                {
                    Properties.Settings.Default.SavedSerialPort = SerialPort?.PortName;
                    Properties.Settings.Default.SavedSerialDevice = SerialPortDeviceType;
                    Properties.Settings.Default.SavedJ2534Device = "";
                }
                else
                {
                    Properties.Settings.Default.SavedSerialPort = "";
                    Properties.Settings.Default.SavedSerialDevice = "";
                    Properties.Settings.Default.SavedJ2534Device = J2534DeviceType;
                }
                Properties.Settings.Default.SavedDevice4xCommunicationEnabled = Enable4xReadWrite;
                Properties.Settings.Default.Save();
                RequestAcceptAndClose?.Invoke();
            }
            else
            {
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
            else if (DeviceCategory == DeviceConfiguration.Constants.DeviceCategoryJ2534)
            {
                device = DeviceFactory.CreateJ2534Device(J2534DeviceType, logger);
                target = J2534DeviceType ?? "J2534 device";
            }
            else
            {
                StatusText = "No device specified.";
                MessageBox.Show(
                    "Choose a device to test first.",
                    "Test Device",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (device == null)
            {
                StatusText = "Could not create " + target + ".";
                MessageBox.Show(
                    "FAIL" + Environment.NewLine + Environment.NewLine +
                        "Could not create " + target + ".",
                    "Test Device",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Friendly name for the device (GetDeviceType()), plus the port it is on (serial only).
            string description = device.GetDeviceType() + onPort;

            StatusText = "Testing " + device.GetDeviceType() + "...";
            try
            {
                // Guard the test with a timeout: a defunct port (e.g. a stale Bluetooth COM
                // port) can make Initialize() hang, which would otherwise freeze the dialog.
                Task<bool> initializeTask = device.Initialize();
                bool completed = await initializeTask.AwaitWithTimeout(TimeSpan.FromSeconds(5));
                if (!completed)
                {
                    StatusText = "Timed out testing " + description + ".";
                    MessageBox.Show(
                        "FAIL" + Environment.NewLine + Environment.NewLine +
                            "Timed out trying to use " + description + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or the device may not be responding.",
                        "Test Device",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else if (initializeTask.Result)
                {
                    StatusText = description + " test OK.";
                    MessageBox.Show(
                        "OK" + Environment.NewLine + Environment.NewLine +
                            description + " initialized successfully.",
                        "Test Device",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusText = description + " test FAILED.";
                    MessageBox.Show(
                        "FAIL" + Environment.NewLine + Environment.NewLine +
                            "Unable to initialize " + description + ".",
                        "Test Device",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                StatusText = description + " test FAILED: " + exception.Message;
                MessageBox.Show(
                    "FAIL" + Environment.NewLine + Environment.NewLine +
                        "Unable to use " + description + ":" + Environment.NewLine + Environment.NewLine + exception.Message,
                    "Test Device",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                MessageBox.Show(
                    "Choose a serial port first, then click Auto Detect to scan it.",
                    "Auto Detect",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                    MessageBox.Show(
                        "Auto detect timed out on " + SerialPort?.PortName + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or a connected device may not be responding.",
                        "Auto Detect",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                device = detectTask.Result;
                if (device == null)
                {
                    StatusText = "Auto detect timed out on " + SerialPort?.PortName + ".";
                    MessageBox.Show(
                        "Auto detect timed out on " + SerialPort?.PortName + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or a connected device may not be responding.",
                        "Auto Detect",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                device = detectTask.Result;
                if (device == null)
                {
                    StatusText = "No compatible device found on " + SerialPort?.PortName + ".";
                    MessageBox.Show(
                        "No compatible devices found on " + SerialPort?.PortName + ".",
                        "Auto Detect",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string deviceType = device.GetDeviceType();

                StatusText = "Found " + deviceType + " on " + SerialPort?.PortName + ".";
                MessageBox.Show(
                    "Found a " + deviceType + " device on " + SerialPort?.PortName + ".",
                    "Auto Detect",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                logger.AddDebugMessage("Auto detect failed: " + exception.ToString());
                StatusText = "Auto detect failed: " + exception.Message;
                MessageBox.Show(
                    "Auto detect failed on " + SerialPort?.PortName + ":" + Environment.NewLine + Environment.NewLine + exception.Message,
                    "Auto Detect",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            if (DeviceConfiguration.Settings.DeviceCategory.Equals("Serial") && SerialDevices.Count > 1)
            {
                DeviceCategory = "Serial";
            }
            else if (DeviceConfiguration.Settings.DeviceCategory.Equals("J2534") && J2534Devices.Count > 1)
            {
                DeviceCategory = "J2534";
            }

            SerialPort = SerialPorts.FirstOrDefault(p => p.PortName == DeviceConfiguration.Settings.SerialPort);
            SerialPortDeviceType = DeviceConfiguration.Settings.SerialPortDeviceType;
            J2534DeviceType = DeviceConfiguration.Settings.J2534DeviceType;
            Enable4xReadWrite = DeviceConfiguration.Settings.Enable4xReadWrite;
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SerialPorts.Clear();
                    foreach (var port in ports)
                        SerialPorts.Add(port);
                });
            }
            catch (TimeoutException)
            {
                string savedPort = DeviceConfiguration.Settings.SerialPort;
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
            try
            {
                Task<List<J2534DotNet.J2534Device>> devicesTask = Task.Run(() => J2534DeviceFinder.FindInstalledJ2534DLLs(logger));
                if (await devicesTask.AwaitWithTimeout(TimeSpan.FromSeconds(5)))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        J2534Devices.Clear();
                        foreach (var device in devicesTask.Result)
                            J2534Devices.Add(device);
                    });
                }
                else
                {
                    string savedDevice = DeviceConfiguration.Settings.J2534DeviceType;
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

        public async Task TestSelectedDeviceAsync()
        {
            Device? device = null;
            string target;
            string onPort = string.Empty;

            if (SerialPort == null) return;

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
            else if (IsJ2534DeviceSelected)
            {
                device = DeviceFactory.CreateJ2534Device(J2534DeviceType, logger);
                target = J2534DeviceType ?? "J2534 device";
            }
            else
            {
                StatusText = "No device specified.";
                if (ShowInfoAlertAsync != null)
                {
                    await ShowInfoAlertAsync("Choose a device to test first.", "Test Device");
                }
                return;
            }

            if (device == null)
            {
                StatusText = $"Could not create {target}.";
                if (ShowErrorAlertAsync != null)
                {
                    await ShowErrorAlertAsync($"FAIL{Environment.NewLine}{Environment.NewLine}Could not create {target}.", "Test Device");
                }
                return;
            }

            string description = device.GetDeviceType() + onPort;
            IsBusy = true;
            StatusText = $"Testing {device.GetDeviceType()}...";

            try
            {
                Task<bool> initializeTask = device.Initialize();
                bool completed = await initializeTask.AwaitWithTimeout(TimeSpan.FromSeconds(5));
                SelectedDevice = device;
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
            }
        }

        [GeneratedRegex(@"COM\d+")]
        private static partial Regex SerialPortRegex();
    }
}