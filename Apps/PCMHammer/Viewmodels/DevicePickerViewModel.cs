using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.Views;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace PCMHammer.Viewmodels
{
    public class DevicePickerViewModel : INotifyPropertyChanged
    {
        private const string _prompt = "Select...";
        private readonly ILogger _logger;

        public Device? SelectedDevice = null;
        public event Action? RequestClose;
        public event Action? RequestAcceptAndClose;

        public DevicePickerViewModel(ILogger logger)
        {
            _logger = logger;
            SelectSerialCommand = new RelayCommand(ExecuteSelectSerial);
            SelectJ2534Command = new RelayCommand(ExecuteSelectJ2534);

            AutoDetectCommand = new RelayCommand(ExecuteAutoDetect);
            TestCommand = new RelayCommand(ExecuteTestSelectedDevice);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            AcceptCommand = new RelayCommand(ExecuteAcceptAndClose);
        }

        // UI Binding Properties
        private string? _deviceCategory;
        public string? DeviceCategory
        {
            get => _deviceCategory;
            set
            {
                if (_deviceCategory != value)
                {
                    _deviceCategory = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSerialSelected));
                    OnPropertyChanged(nameof(IsJ2534Selected));
                }
            }
        }

        private string? _j2534DeviceType;
        public string? J2534DeviceType
        {
            get => _j2534DeviceType;
            set
            {
                if (_j2534DeviceType != value)
                {
                    _j2534DeviceType = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _serialPort;
        public string? SerialPort
        {
            get => _serialPort;
            set
            {
                if (_serialPort != value)
                {
                    _serialPort = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _serialPortDeviceType;
        public string? SerialPortDeviceType
        {
            get => _serialPortDeviceType;
            set
            {
                if (_serialPortDeviceType != value)
                {
                    _serialPortDeviceType = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _enable4xReadWrite;
        public bool Enable4xReadWrite
        {
            get => _enable4xReadWrite;
            set
            {
                if (_enable4xReadWrite != value)
                {
                    _enable4xReadWrite = value;
                    OnPropertyChanged();
                }
            }
        }
        
        // Collections for UI drop-downs
        public List<object> SerialPorts { get; } = [];
        public List<string> SerialDevices { get; } = [];
        public List<object> J2534Devices { get; } = [];

        // Notification States
        public string StatusText { get; set; } = "Ready.";
        public bool IsBusy { get; set; }
        public bool IsSerialSelected => DeviceCategory.Equals("Serial");
        public bool IsJ2534Selected => DeviceCategory.Equals("J2534");

        // Commands
        public ICommand SelectSerialCommand { get; }
        public ICommand SelectJ2534Command { get; }
        public ICommand AutoDetectCommand { get; }
        public ICommand TestCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AcceptCommand { get; }

        // Callbacks for View-layer UI alerts (decouples MessageBox.Show)
        public Func<string, string, Task>? ShowWarningAlertAsync { get; set; }
        public Func<string, string, Task>? ShowInfoAlertAsync { get; set; }
        public Func<string, string, Task>? ShowErrorAlertAsync { get; set; }

        private async void ExecuteAcceptAndClose()
        {
            await TestSelectedDeviceAsync();
            if (SelectedDevice != null)
            {
                RequestAcceptAndClose?.Invoke();
            }
        }
        private void ExecuteTestSelectedDevice()
        {
            // This method should be implemented to test the selected device.
        }

        private void ExecuteAutoDetect()
        {
            // This method should be implemented to auto-detect the device.
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
            // Scaffold placeholders synchronously to protect the view's state changes
            SerialPorts.Add(_prompt);
            J2534Devices.Add(_prompt);

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

            SerialPort = DeviceConfiguration.Settings.SerialPort;
            SerialPortDeviceType = DeviceConfiguration.Settings.SerialPortDeviceType;
            J2534DeviceType = DeviceConfiguration.Settings.J2534DeviceType;
            Enable4xReadWrite = DeviceConfiguration.Settings.Enable4xReadWrite;
        }

        private void FillSerialDeviceList()
        {
            SerialDevices.Add(_prompt);
            SerialDevices.Add(ElmDevice.DeviceType);
            SerialDevices.Add(AvtDevice.DeviceType);
            SerialDevices.Add(OBDXProDevice.DeviceType);
            // SerialDevices.Add(MockDevice.DeviceType); // Uncomment for testing
        }

        private async Task AddDiscoveredPortsAsync()
        {
            try
            {
                Task<List<SerialPortInfo>> portsTask = Task.Run(() => PortDiscovery.GetPorts(_logger).ToList());
                if (await portsTask.AwaitWithTimeout(TimeSpan.FromSeconds(5)))
                {
                    SerialPorts.AddRange(portsTask.Result.Cast<object>());
                }
                else
                {
                    string savedPort = DeviceConfiguration.Settings.SerialPort;
                    StatusText = string.IsNullOrEmpty(savedPort)
                        ? "Timed out listing serial ports - a disconnected device may be stuck. Try a different port."
                        : $"Timed out listing serial ports - the saved port {savedPort} looks disconnected or stuck. Avoid it and choose a different port.";
                }
            }
            catch (Exception ex)
            {
                _logger.AddDebugMessage("Failed to list serial ports: " + ex.ToString());
                StatusText = "Unable to list serial ports: " + ex.Message;
            }
            // SerialPorts.Add(MockPort.PortName); // Uncomment for testing
        }

        private async Task AddDiscoveredJ2534DevicesAsync()
        {
            try
            {
                Task<List<J2534DotNet.J2534Device>> devicesTask = Task.Run(() => J2534DeviceFinder.FindInstalledJ2534DLLs(_logger));
                if (await devicesTask.AwaitWithTimeout(TimeSpan.FromSeconds(5)))
                {
                    J2534Devices.AddRange(devicesTask.Result.Cast<object>());
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
                _logger.AddDebugMessage("Failed to list J2534 devices: " + ex.ToString());
                StatusText = "Unable to list J2534 devices: " + ex.Message;
            }
        }

        public async Task AutoDetectSerialAsync()
        {
            if (string.IsNullOrEmpty(SerialPort) || SerialPort == _prompt)
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
                Task<Device?> detectTask = DeviceFactory.AutoDetectSerialDevice(SerialPort, _logger);
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
                _logger.AddDebugMessage("Auto detect failed: " + ex.ToString());
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

            var match = Regex.Match(SerialPort, @"\((COM\d+)\)");
            if (match.Success)
            {
                // Extracts "COM1" out of "Communications Port (COM1)"
                SerialPort = match.Groups[1].Value;
            }
            else if (SerialPort.Contains("COM"))
            {
                // Fallback: If it's just raw text containing COM but no parentheses, 
                // cleanly parse out the exact COM segment
                var standaloneMatch = Regex.Match(SerialPort, @"COM\d+");
                if (standaloneMatch.Success)
                {
                    SerialPort = standaloneMatch.Value;
                }
            }

            _logger.AddDebugMessage($"Resolved friendly name '{this.SerialPort}' to hardware identifier '{SerialPort}'");

            if (IsSerialSelected)
            {
                device = DeviceFactory.CreateSerialDevice(SerialPort, SerialPortDeviceType, _logger);
                onPort = " on " + (SerialPort ?? "(no port)");
                target = (SerialPortDeviceType ?? "serial device") + onPort;
            }
            else if (IsJ2534Selected)
            {
                device = DeviceFactory.CreateJ2534Device(J2534DeviceType, _logger);
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

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}