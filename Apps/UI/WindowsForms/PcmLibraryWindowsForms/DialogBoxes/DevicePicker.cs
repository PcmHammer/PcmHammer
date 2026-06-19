// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using J2534DotNet;

namespace PcmHacking
{
    /// <summary>
    /// This dialog box allows the user to choose the type of device and (for serial devices) the COM port.
    /// </summary>
    public partial class DevicePicker : Form
    {
        /// <summary>
        /// Indicate which category of device the user has chosen.
        /// </summary>
        public string? DeviceCategory { get; set; }

        /// <summary>
        /// Indicates the name of the J2534 device that the user has chosen.
        /// </summary>
        public string? J2534DeviceType { get; set; }

        /// <summary>
        /// Indicates the serial port (COM port) that the user has chosen. Only relevant for serial devices.
        /// </summary>
        public string? SerialPort { get; set; }

        /// <summary>
        /// Indicates which type of serial device the user has chosen.
        /// </summary>
        public string? SerialPortDeviceType { get; set; }

        /// <summary>
        /// Enable Disable VPW 4x.
        /// </summary>
        public bool Enable4xReadWrite { get; set; }

        /// <summary>
        /// Prompt to put into drop-down lists to let the user know that they need to make a selection.
        /// </summary>
        private const string prompt = "Select...";

        /// <summary>
        /// This allows the dialog box to add messages to the Results pane and Debug pane.
        /// </summary>
        private ILogger logger;

        /// <summary>
        /// Constructor.
        /// </summary>
        public DevicePicker(ILogger logger)
        {
            this.logger = logger;

            InitializeComponent();
        }

        /// <summary>
        /// Populate controls with the current selections before displaying the form.
        /// </summary>
        private async void DevicePicker_Load(object sender, EventArgs e)
        {
            // Populate every combo box's placeholder and static items SYNCHRONOUSLY, before the
            // first await below. The form's message pump runs during those awaits and activates
            // the dialog; activation focuses serialRadioButton, which fires CheckedChanged and
            // sets SelectedIndex on these lists. If a list were still empty at that point,
            // SelectedIndex = 0 would throw ArgumentOutOfRangeException. The slow-to-discover
            // ports / J2534 devices are appended afterwards.
            this.serialPortList.Items.Add(prompt);
            this.serialPortList.SelectedIndex = 0;

            this.FillSerialDeviceList();

            this.j2534DeviceList.Items.Add(prompt);
            this.j2534DeviceList.SelectedIndex = 0;

            await this.AddDiscoveredPorts();

            await this.AddDiscoveredJ2534Devices();

            if(this.serialDeviceList.Items.Count > 0)
            {
                this.serialRadioButton.Checked = true;
            }
            else if (this.j2534DeviceList.Items.Count > 0)
            {
                this.j2534RadioButton.Checked = true;
            }
            else
            {
                this.serialRadioButton.Checked = true;
                this.status.Text = "You don't seem to have any serial ports or J2534 devices.";
            }

            switch(DeviceConfiguration.Settings.DeviceCategory)
            {
                case DeviceConfiguration.Constants.DeviceCategorySerial:
                    {
                        if (this.serialDeviceList.Items.Count > 0)
                        {
                            this.serialRadioButton.Checked = true;
                        }
                    }
                    break;

                case DeviceConfiguration.Constants.DeviceCategoryJ2534:
                    {
                        if (this.j2534DeviceList.Items.Count > 0)
                        {
                            this.j2534RadioButton.Checked = true;
                        }
                    }
                    break;
            }

            SetDefault(
                this.serialPortList, 
                x => (x as SerialPortInfo)?.PortName,
                DeviceConfiguration.Settings.SerialPort);

            SetDefault(
                this.serialDeviceList,
                x => x.ToString(),
                DeviceConfiguration.Settings.SerialPortDeviceType);

            SetDefault(
                this.j2534DeviceList, 
                x => x.ToString(),
                DeviceConfiguration.Settings.J2534DeviceType);

            this.Enable4xReadWrite = this.enable4xReadWriteCheckBox.Checked = DeviceConfiguration.Settings.Enable4xReadWrite;
        }

        /// <summary>
        /// Set a ComboBox to the given item.
        /// </summary>
        private static void SetDefault(ComboBox list, Func<object, string?> getConfigurationValue, string value)
        {
            foreach(object item in list.Items)
            {
                if (getConfigurationValue(item) == value)
                {
                    list.SelectedItem = item;
                    return;
                }
            }
        }

        /// <summary>
        /// Append currently-available serial ports to the (already-scaffolded) port list.
        /// </summary>
        private async Task AddDiscoveredPorts()
        {
            // Enumerate serial ports off the UI thread, bounded by a timeout. The underlying
            // WMI PnP query (Win32_PnPEntity) can hang indefinitely when a serial enumerator is
            // wedged - most commonly a stale or disconnected Bluetooth COM port - which would
            // otherwise freeze the whole dialog (and the app) the instant it opens.
            List<SerialPortInfo> ports = new List<SerialPortInfo>();
            try
            {
                Task<List<SerialPortInfo>> portsTask = Task.Run(
                    () => PortDiscovery.GetPorts(this.logger).ToList());
                if (await portsTask.AwaitWithTimeout(TimeSpan.FromSeconds(5)))
                {
                    ports = portsTask.Result;
                }
                else
                {
                    string savedPort = DeviceConfiguration.Settings.SerialPort;
                    this.status.Text = string.IsNullOrEmpty(savedPort)
                        ? "Timed out listing serial ports - a disconnected device may be stuck. Try a different port."
                        : string.Format(
                            "Timed out listing serial ports - the saved port {0} looks disconnected or stuck. Avoid it and choose a different port.",
                            savedPort);
                }
            }
            catch (Exception exception)
            {
                this.logger.AddDebugMessage("Failed to list serial ports: " + exception.ToString());
                this.status.Text = "Unable to list serial ports: " + exception.Message;
            }

            foreach (SerialPortInfo portInfo in ports)
            {
                this.serialPortList.Items.Add(portInfo);
            }

            // This is useful for testing without an actual PCM.
            // You'll need to uncomment a line in FillSerialDeviceList as well as this one.
            // this.serialPortList.Items.Add(MockPort.PortName);
        }

        /// <summary>
        /// Fill the list of serial devices with the device types that the app supports.
        /// </summary>
        private void FillSerialDeviceList()
        {
            this.serialDeviceList.Items.Add(prompt);
            this.serialDeviceList.SelectedIndex = 0;
            this.serialDeviceList.Items.Add(ElmDevice.DeviceType);
            this.serialDeviceList.Items.Add(AvtDevice.DeviceType);
            this.serialDeviceList.Items.Add(OBDXProDevice.DeviceType);
            this.serialDeviceList.Items.Add(SlcanDevice.DeviceType);

            // This is useful for testing without an actual PCM.
            // You'll need to uncomment a line in FillPortList as well as this one.
            // this.serialDeviceList.Items.Add(MockDevice.DeviceType);
        }

        /// <summary>
        /// Append discovered J2534 devices to the (already-scaffolded) device list.
        /// </summary>
        private async Task AddDiscoveredJ2534Devices()
        {
            // The J2534 scan only reads the registry today, so it is fast - but third-party
            // J2534 DLLs can be loaded during detection, and a misbehaving driver could block.
            // Run it off the UI thread with a timeout for the same reason as the serial list.
            List<J2534DotNet.J2534Device> devices = new List<J2534DotNet.J2534Device>();
            try
            {
                Task<List<J2534DotNet.J2534Device>> devicesTask = Task.Run(
                    () => J2534DeviceFinder.FindInstalledJ2534DLLs(this.logger));
                if (await devicesTask.AwaitWithTimeout(TimeSpan.FromSeconds(5)))
                {
                    devices = devicesTask.Result;
                }
                else
                {
                    string savedDevice = DeviceConfiguration.Settings.J2534DeviceType;
                    this.status.Text = string.IsNullOrEmpty(savedDevice)
                        ? "Timed out listing J2534 devices - a driver may be stuck. Try a different device."
                        : string.Format(
                            "Timed out listing J2534 devices - the saved device {0} may have a stuck driver. Avoid it and choose a different device.",
                            savedDevice);
                }
            }
            catch (Exception exception)
            {
                this.logger.AddDebugMessage("Failed to list J2534 devices: " + exception.ToString());
                this.status.Text = "Unable to list J2534 devices: " + exception.Message;
            }

            foreach (J2534DotNet.J2534Device device in devices)
            {
                this.j2534DeviceList.Items.Add(device);
            }
        }

        /// <summary>
        /// Scan the selected serial port for a compatible device, then report what was found.
        /// </summary>
        private async void autoDetectButton_Click(object sender, EventArgs e)
        {
            string? portName = (this.serialPortList.SelectedItem as SerialPortInfo)?.PortName
                ?? (this.serialPortList.SelectedItem as string);
            if (portName == prompt)
            {
                portName = null;
            }

            if (string.IsNullOrEmpty(portName))
            {
                MessageBox.Show(
                    this,
                    "Choose a serial port first, then click Auto Detect to scan it.",
                    "Auto Detect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this.SetActionsEnabled(false);
            this.status.Text = "Scanning " + portName + " for a compatible device...";
            Device? device = null;
            try
            {
                // Bound the scan with a timeout: a defunct or busy port can make the underlying
                // open / probe sequence hang, which would otherwise freeze the dialog.
                // portName is non-null here (guarded by the IsNullOrEmpty check above).
                Task<Device?> detectTask = DeviceFactory.AutoDetectSerialDevice(portName!, this.logger);
                if (!await detectTask.AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                {
                    this.status.Text = "Auto detect timed out on " + portName + ".";
                    MessageBox.Show(
                        this,
                        "Auto detect timed out on " + portName + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or a connected device may not be responding.",
                        "Auto Detect",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                device = detectTask.Result;
                if (device == null)
                {
                    this.status.Text = "No compatible device found on " + portName + ".";
                    MessageBox.Show(
                        this,
                        "No compatible devices found on " + portName + ".",
                        "Auto Detect",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                string deviceType = device.GetDeviceType();

                // Reflect the detected device back into the dialog's selections so the user can
                // just click OK to keep it.
                this.serialRadioButton.Checked = true;
                SetDefault(this.serialPortList, x => (x as SerialPortInfo)?.PortName, portName!);
                SetDefault(this.serialDeviceList, x => x.ToString(), deviceType);

                this.status.Text = "Found " + deviceType + " on " + portName + ".";
                MessageBox.Show(
                    this,
                    "Found a " + deviceType + " device on " + portName + ".",
                    "Auto Detect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                this.logger.AddDebugMessage("Auto detect failed: " + exception.ToString());
                this.status.Text = "Auto detect failed: " + exception.Message;
                MessageBox.Show(
                    this,
                    "Auto detect failed on " + portName + ":" + Environment.NewLine + Environment.NewLine + exception.Message,
                    "Auto Detect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // AutoDetectSerialDevice opens the port; dispose the device (and its port) so the
                // OK / Test path can reopen it. Dispose is non-blocking for serial ports.
                device?.Dispose();
                this.SetActionsEnabled(true);
            }
        }

        /// <summary>
        /// Enable or disable the buttons that start a (potentially slow) port operation, so the
        /// user can't launch an Auto Detect and a Test at the same time on the same port.
        /// </summary>
        private void SetActionsEnabled(bool enabled)
        {
            this.autoDetectButton.Enabled = enabled;
            this.testButton.Enabled = enabled;
            this.okButton.Enabled = enabled;
        }

        /// <summary>
        /// Close the form, let the caller know that the user clicked OK.
        /// </summary>
        private void okButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// Close the form, let the caller know that the user clicked Cancel.
        /// </summary>
        private void cancelButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }

        /// <summary>
        /// Enable/disable different groups of controls depending of which device type the user has chosen.
        /// </summary>
        private void serialRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (this.serialRadioButton.Checked)
            {
                j2534OptionsGroupBox.Enabled = false;
                J2534DeviceType = string.Empty;
                j2534DeviceList.SelectedIndex = 0;
                serialOptionsGroupBox.Enabled = true;
                this.DeviceCategory = DeviceConfiguration.Constants.DeviceCategorySerial;
            }
        }

        /// <summary>
        /// Enable/disable different groups of controls depending of which device type the user has chosen.
        /// </summary>
        private void j2534RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (this.j2534RadioButton.Checked)
            {
                serialOptionsGroupBox.Enabled = false;
                SerialPortDeviceType = string.Empty;
                serialDeviceList.SelectedIndex = 0;
                serialPortList.SelectedIndex = 0;
                j2534OptionsGroupBox.Enabled = true;
                this.DeviceCategory = DeviceConfiguration.Constants.DeviceCategoryJ2534;
            }
        }

        /// <summary>
        /// Store the name of the newly selected serial port.
        /// </summary>
        private void serialPortList_SelectedIndexChanged(object sender, EventArgs e)
        {
            //mock port isnt a SerialPortInfo
            if (this.serialPortList.SelectedItem is String)
            {
                this.SerialPort = this.serialPortList.SelectedItem as String;
                return;
            }

            this.SerialPort = (this.serialPortList.SelectedItem as SerialPortInfo)?.PortName;
        }

        /// <summary>
        /// Store the name of the newly selected serial device type.
        /// </summary>
        private void serialDeviceList_SelectedIndexChanged(object sender, EventArgs e)
        {
            string? item = this.serialDeviceList.SelectedItem?.ToString();
            if (item == prompt)
            {
                item = null;
            }

            this.SerialPortDeviceType = item;
        }

        /// <summary>
        /// Store the name of the newly selected J2534 device.
        /// </summary>
        private void j2534DeviceList_SelectedIndexChanged(object sender, EventArgs e)
        {
            string? item = this.j2534DeviceList.SelectedItem?.ToString();
            if (item == prompt)
            {
                item = null;
            }

            this.J2534DeviceType = item;
        }

        /// <summary>
        /// Enable/disable VPW 4x
        /// </summary>
        private void enable4xReadWriteCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.Enable4xReadWrite = this.enable4xReadWriteCheckBox.Checked;
        }

        /// <summary>
        /// Test the user's selections.
        /// </summary>
        private async void testButton_Click(object sender, EventArgs e)
        {
            Device? device;
            // "target" describes the user's selection for the failure messages we can show even
            // before a device object exists (e.g. nothing matched).
            string target;
            // "onPort" is the trailing " on COMx" suffix for serial devices, empty for J2534.
            string onPort = string.Empty;
            if (this.DeviceCategory == DeviceConfiguration.Constants.DeviceCategorySerial)
            {
                device = DeviceFactory.CreateSerialDevice(this.SerialPort, this.SerialPortDeviceType, this.logger);
                onPort = " on " + (this.SerialPort ?? "(no port)");
                target = (this.SerialPortDeviceType ?? "serial device") + onPort;
            }
            else if (this.DeviceCategory == DeviceConfiguration.Constants.DeviceCategoryJ2534)
            {
                device = DeviceFactory.CreateJ2534Device(this.J2534DeviceType, this.logger);
                target = this.J2534DeviceType ?? "J2534 device";
            }
            else
            {
                this.status.Text = "No device specified.";
                MessageBox.Show(
                    this,
                    "Choose a device to test first.",
                    "Test Device",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (device == null)
            {
                this.status.Text = "Could not create " + target + ".";
                MessageBox.Show(
                    this,
                    "FAIL" + Environment.NewLine + Environment.NewLine +
                        "Could not create " + target + ".",
                    "Test Device",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Friendly name for the device (GetDeviceType()), plus the port it is on (serial only).
            string description = device.GetDeviceType() + onPort;

            this.SetActionsEnabled(false);
            this.status.Text = "Testing " + device.GetDeviceType() + "...";
            try
            {
                // Guard the test with a timeout: a defunct port (e.g. a stale Bluetooth COM
                // port) can make Initialize() hang, which would otherwise freeze the dialog.
                Task<bool> initializeTask = device.Initialize();
                bool completed = await initializeTask.AwaitWithTimeout(TimeSpan.FromSeconds(5));
                if (!completed)
                {
                    this.status.Text = "Timed out testing " + description + ".";
                    MessageBox.Show(
                        this,
                        "FAIL" + Environment.NewLine + Environment.NewLine +
                            "Timed out trying to use " + description + "." + Environment.NewLine + Environment.NewLine +
                            "The port may be in use, or the device may not be responding.",
                        "Test Device",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else if (initializeTask.Result)
                {
                    this.status.Text = description + " test OK.";
                    MessageBox.Show(
                        this,
                        "OK" + Environment.NewLine + Environment.NewLine +
                            description + " initialized successfully.",
                        "Test Device",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    this.status.Text = description + " test FAILED.";
                    MessageBox.Show(
                        this,
                        "FAIL" + Environment.NewLine + Environment.NewLine +
                            "Unable to initialize " + description + ".",
                        "Test Device",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception exception)
            {
                this.status.Text = description + " test FAILED: " + exception.Message;
                MessageBox.Show(
                    this,
                    "FAIL" + Environment.NewLine + Environment.NewLine +
                        "Unable to use " + description + ":" + Environment.NewLine + Environment.NewLine + exception.Message,
                    "Test Device",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // Dispose is non-blocking for serial ports (see StandardPort), so this is safe
                // on the UI thread even when the underlying device is dead.
                device.Dispose();
                this.SetActionsEnabled(true);
            }
        }
    }
}
