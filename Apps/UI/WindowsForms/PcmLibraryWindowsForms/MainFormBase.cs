// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    public partial class MainFormBase : Form, ILogger
    {
        /// <summary>
        /// Impolite, but it needs to be short enough to fit in the device-description box.
        /// </summary>
        private const string selectAnotherDevice = "Select another device.";

        /// <summary>
        /// The Vehicle object is our interface to the car. It has the device, the message generator, and the message parser.
        /// </summary>
        private Vehicle vehicle = null!;
        protected Vehicle Vehicle { get { return this.vehicle; } }

        public virtual void AddDebugMessage(string message) { }
        public virtual void AddUserMessage(string message) { }
        public virtual void StatusUpdateActivity(string activity) { }
        public virtual void StatusUpdateTimeRemaining(string remaining) { }
        public virtual void StatusUpdatePercentDone(string percent) { }
        public virtual void StatusUpdateRetryCount(string retries) { }
        public virtual void StatusUpdateProgressBar(double completed, bool visible) { }
        public virtual void StatusUpdateKbps(string Kbps) { }
        public virtual void StatusUpdateReset() { }
        public virtual void ResetLogs() { }

        public virtual string GetAppNameAndVersion() { return "MainFormBase.GetAppNameAndVersion is not implemented"; }

        protected virtual void EnableInterfaceSelection() { }
        protected virtual void EnableUserInput() { }
        protected virtual void DisableUserInput() { }

        protected virtual void SetSelectedDeviceText(string message)
        {

        }

        protected virtual void NoDeviceSelected()
        {
            // disable re-init button
            // set device name to "no device selected"
        }

        protected virtual async Task ValidDeviceSelectedAsync(string deviceName)
        {
            // enable re-init button
            // show device name

            // This is just here to suppress a compiler warning.
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handle clicking the "Select Interface" button
        /// </summary>
        /// <returns></returns>
        public async Task<bool> HandleSelectButtonClick()
        {
            // Release the currently-connected device before showing the picker so its port is
            // free. Otherwise the dialog's Auto Detect / Test can't open a port that the live
            // connection is already holding (e.g. "Access to the port 'COM3' is denied" when the
            // picker pre-selects the device that is already in use).
            bool hadDevice = this.vehicle != null;
            if (this.vehicle != null)
            {
                this.vehicle.Dispose();
                this.vehicle = null!;
            }

            using (DevicePicker picker = new DevicePicker(this))
            {
                DialogResult result = picker.ShowDialog();
                if (result == DialogResult.OK)
                {
                    if (picker.DeviceCategory == DeviceConfiguration.Constants.DeviceCategorySerial)
                    {
                        if (string.IsNullOrEmpty(picker.SerialPort))
                        {
                            return false;
                        }

                        if (string.IsNullOrEmpty(picker.SerialPortDeviceType))
                        {
                            return false;
                        }
                    }

                    if (picker.DeviceCategory == DeviceConfiguration.Constants.DeviceCategoryJ2534)
                    {
                        if (string.IsNullOrEmpty(picker.J2534DeviceType))
                        {
                            return false;
                        }
                    }

                    DeviceConfiguration.Settings.Enable4xReadWrite = picker.Enable4xReadWrite;
                    DeviceConfiguration.Settings.DeviceCategory = picker.DeviceCategory;
                    DeviceConfiguration.Settings.J2534DeviceType = picker.J2534DeviceType;
                    DeviceConfiguration.Settings.SerialPort = picker.SerialPort;
                    DeviceConfiguration.Settings.SerialPortDeviceType = picker.SerialPortDeviceType;
                    DeviceConfiguration.Settings.Save();
                    return await this.ResetDevice();
                }
            }

            // The user cancelled. Re-open whatever device was connected before, so closing the
            // dialog doesn't silently disconnect them.
            if (hadDevice)
            {
                return await this.ResetDevice();
            }

            return false;
        }

        /// <summary>
        /// Close the old interface device and open a new one.
        /// </summary>
        protected async Task<bool> ResetDevice()
        {
            if (this.vehicle != null)
            {
                this.vehicle.Dispose();
                this.vehicle = null!;
            }
            Device? device = DeviceFactory.CreateDeviceFromConfigurationSettings(this);
            if (device == null)
            {
                this.Invoke((MethodInvoker)delegate()
                {
                    this.NoDeviceSelected();
                    this.SetSelectedDeviceText(selectAnotherDevice);
                    this.DisableUserInput();
                    this.EnableInterfaceSelection();
                });
                return false;
            }

            this.Invoke((MethodInvoker)delegate ()
            {
                this.SetSelectedDeviceText("Connecting, please wait...");
            });

            Protocol protocol = new Protocol();
            this.vehicle = new Vehicle(
                device,
                protocol,
                this,
                new ToolPresentNotifier(device, protocol, this),
                string.Empty); //Logic will treat an empty string as a trigger method to fetch path.

            if (!await this.InitializeCurrentDevice())
            {
                // Initialization failed (e.g. a defunct port). Dispose the vehicle so the
                // device and its serial port are released, instead of leaking an open port
                // and a running Receiver loop that we can never reach again.
                if (this.vehicle != null)
                {
                    this.vehicle.Dispose();
                    this.vehicle = null!;
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Initialize the current device.
        /// </summary>
        protected async Task<bool> InitializeCurrentDevice()
        {
            if (this.vehicle == null)
            {
                return false;
            }

            this.Invoke((MethodInvoker)delegate ()
            {
                this.DisableUserInput();
                this.ResetLogs();
            });

            // Show the copyright on the same line as the app name, e.g.
            // "PCM Hammer - Copyright (C) ...", then the build/version and "Running at" lines.
            string[] appLines = GetAppNameAndVersion().Split('\n');
            appLines[0] = appLines[0] + " - " + AppInfo.CopyrightNotice;
            foreach (string line in appLines)
                this.AddUserMessage(line);
            this.AddUserMessage(AppInfo.GetRunningAtMessage());

            try
            {
                // TODO: this should not return a boolean, it should just throw 
                // an exception if it is not able to initialize the device.
                Task<bool> initializationTask = this.vehicle.ResetConnection();
                bool completed = await initializationTask.AwaitWithTimeout(TimeSpan.FromSeconds(5));
                if (!completed)
                {
                    throw new TimeoutException("Vehicle.ResetConnection timed out.");
                }

                if (!initializationTask.Result)
                {
                    this.AddUserMessage("Unable to initialize " + this.vehicle.DeviceDescription);

                    this.Invoke((MethodInvoker)delegate ()
                    {
                        this.NoDeviceSelected();
                        this.SetSelectedDeviceText(selectAnotherDevice);
                        this.EnableInterfaceSelection();
                    });
                    return false;
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage("Unable to initialize " + this.vehicle.DeviceDescription);

                this.AddDebugMessage(exception.ToString());
                this.Invoke((MethodInvoker)delegate ()
                {
                    this.NoDeviceSelected();
                    this.SetSelectedDeviceText(selectAnotherDevice);
                    this.EnableInterfaceSelection();
                });
                return false;
            }

            this.Invoke((MethodInvoker)delegate ()
            {
                if (!this.vehicle.Supports4X)
                {
                    DeviceConfiguration.Settings.Enable4xReadWrite = true;
                    DeviceConfiguration.Settings.Save();
                }
                this.vehicle.Enable4xReadWrite = DeviceConfiguration.Settings.Enable4xReadWrite;
            });

            await this.ValidDeviceSelectedAsync(this.vehicle.DeviceDescription);

            this.Invoke((MethodInvoker)delegate ()
            {
                this.EnableUserInput();
            });
            return true;
        }
    }
}
