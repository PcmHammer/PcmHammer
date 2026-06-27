// SPDX-License-Identifier: GPL-3.0-only
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    // Bus Monitor: a developer tab (shown only when enabled in Settings) that passively displays raw
    // VPW or CAN traffic from the selected device. It owns the device while running, so normal
    // operations are disabled until the user clicks Stop.
    public partial class MainForm
    {
        private const int MonitorMaxLines = 5000;
        private const string DefaultCanFilter = "7E0 7E8 101";

        private TabPage busMonitorTab;
        private LogListView monitorLog;
        private RadioButton monitorVpwRadio;
        private RadioButton monitorCanRadio;
        private TextBox monitorFilterTextBox;
        private Button monitorStartStopButton;
        private Button monitorClearButton;

        private CancellationTokenSource? monitorCts;
        private bool monitoring;

        /// <summary>
        /// Show the Bus Monitor tab (and its Save menu item) for this session. It is off by default and
        /// only appears via Tools &gt; Bus Monitor; once shown it stays until the app closes.
        /// </summary>
        private void ShowBusMonitorTab()
        {
            if (this.busMonitorTab == null)
            {
                this.BuildBusMonitorTab();
            }

            if (!this.tabs.TabPages.Contains(this.busMonitorTab))
            {
                this.tabs.TabPages.Add(this.busMonitorTab);
                this.saveBusMonitorLogToolStripMenuItem.Visible = true;
                this.RefreshMonitorCapability();
            }

            this.tabs.SelectedTab = this.busMonitorTab;
        }

        private void BuildBusMonitorTab()
        {
            // Size the page to the sibling tabs before placing controls so the Top|Right anchored
            // buttons capture their offsets against the real width (not the default tiny TabPage).
            this.busMonitorTab = new TabPage
            {
                Text = "Bus Monitor",
                UseVisualStyleBackColor = true,
                Padding = new Padding(2),
                Size = new Size(600, 429),
            };

            Label protocolLabel = new Label { Text = "Protocol:", Location = new Point(6, 9), AutoSize = true };
            this.monitorVpwRadio = new RadioButton { Text = "VPW", Location = new Point(62, 6), AutoSize = true, Checked = true };
            this.monitorCanRadio = new RadioButton { Text = "CAN 500k", Location = new Point(118, 6), AutoSize = true };
            Label filterLabel = new Label { Text = "CAN IDs:", Location = new Point(210, 9), AutoSize = true };
            this.monitorFilterTextBox = new TextBox { Text = DefaultCanFilter, Location = new Point(265, 5), Size = new Size(150, 20), Enabled = false };
            this.monitorStartStopButton = new Button { Text = "Start", Location = new Point(440, 4), Size = new Size(70, 23), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            this.monitorClearButton = new Button { Text = "Clear", Location = new Point(514, 4), Size = new Size(70, 23), Anchor = AnchorStyles.Top | AnchorStyles.Right };

            this.monitorLog = new LogListView
            {
                Name = "BusMonitor",
                Location = new Point(6, 34),
                Size = new Size(588, 392),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 8.5F, FontStyle.Regular, GraphicsUnit.Point, 0),
                MaxLines = MonitorMaxLines,
            };

            this.monitorVpwRadio.CheckedChanged += this.monitorProtocol_CheckedChanged;
            this.monitorCanRadio.CheckedChanged += this.monitorProtocol_CheckedChanged;
            this.monitorStartStopButton.Click += this.monitorStartStopButton_Click;
            this.monitorClearButton.Click += (s, e) => this.monitorLog.ClearLog();

            this.busMonitorTab.Controls.Add(this.monitorLog);
            this.busMonitorTab.Controls.Add(protocolLabel);
            this.busMonitorTab.Controls.Add(this.monitorVpwRadio);
            this.busMonitorTab.Controls.Add(this.monitorCanRadio);
            this.busMonitorTab.Controls.Add(filterLabel);
            this.busMonitorTab.Controls.Add(this.monitorFilterTextBox);
            this.busMonitorTab.Controls.Add(this.monitorStartStopButton);
            this.busMonitorTab.Controls.Add(this.monitorClearButton);
        }

        /// <summary>Enable each protocol the current device can monitor; grey everything if none.</summary>
        private void RefreshMonitorCapability()
        {
            if (this.busMonitorTab == null || this.monitoring)
            {
                return;
            }

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                this.BeginInvoke((MethodInvoker)this.RefreshMonitorCapability);
                return;
            }

            IReadOnlyList<BusProtocol> supported = this.Vehicle?.MonitorableProtocols ?? Array.Empty<BusProtocol>();
            bool vpw = supported.Contains(BusProtocol.Vpw);
            bool can = supported.Contains(BusProtocol.Can500k);

            this.monitorVpwRadio.Enabled = vpw;
            this.monitorCanRadio.Enabled = can;

            // Keep the selection on an enabled protocol.
            if (this.monitorVpwRadio.Checked && !vpw)
            {
                this.monitorCanRadio.Checked = can;
            }
            else if (this.monitorCanRadio.Checked && !can)
            {
                this.monitorVpwRadio.Checked = vpw;
            }

            this.monitorStartStopButton.Enabled = vpw || can;
            this.monitorFilterTextBox.Enabled = this.monitorCanRadio.Checked && can;
        }

        private void monitorProtocol_CheckedChanged(object sender, EventArgs e)
        {
            this.monitorFilterTextBox.Enabled = this.monitorCanRadio.Checked && this.monitorCanRadio.Enabled;
        }

        // Tools > Bus Monitor: show the tab for this session and switch to it.
        private void busMonitorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ShowBusMonitorTab();
        }

        // File > Save > Bus Monitor.
        private void saveBusMonitorLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.monitorLog == null)
            {
                return;
            }

            string fileName = this.ShowLogSaveAsDialog(this.monitorLog.Name);
            this.SaveLog(this.monitorLog, fileName);
        }

        private void monitorStartStopButton_Click(object sender, EventArgs e)
        {
            if (this.monitoring)
            {
                this.monitorCts?.Cancel();
            }
            else
            {
                this.StartMonitor();
            }
        }

        private async void StartMonitor()
        {
            if (this.Vehicle == null)
            {
                return;
            }

            BusProtocol protocol = this.monitorCanRadio.Checked ? BusProtocol.Can500k : BusProtocol.Vpw;

            if (protocol == BusProtocol.Vpw && !this.CheckVpwFourXGate())
            {
                return;
            }

            IReadOnlyCollection<uint>? canIds = protocol == BusProtocol.Can500k ? this.ParseCanIds() : null;

            this.monitorCts = new CancellationTokenSource();
            this.monitoring = true;
            this.monitorStartStopButton.Text = "Stop";
            this.monitorVpwRadio.Enabled = false;
            this.monitorCanRadio.Enabled = false;
            this.monitorFilterTextBox.Enabled = false;
            this.DisableUserInput();

            this.AddUserMessage("Bus monitor started on " + protocol + ".");

            BusMonitor monitor = this.Vehicle.CreateBusMonitor();
            try
            {
                await Task.Run(() => monitor.RunAsync(protocol, canIds, line => this.monitorLog.AppendLine(line), this.monitorCts.Token));
            }
            catch (Exception ex)
            {
                this.AddUserMessage("Bus monitor error: " + ex.Message);
                this.AddDebugMessage(ex.ToString());
            }
            finally
            {
                this.monitoring = false;
                this.monitorCts?.Dispose();
                this.monitorCts = null;

                // The form may be closing (which cancels the monitor); skip UI work if so.
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.monitorStartStopButton.Text = "Start";
                    this.EnableUserInput();
                    this.AddUserMessage("Bus monitor stopped.");
                }
            }
        }

        /// <summary>
        /// VPW 4X gate: refuse if 4X is supported but disabled (the user can enable it); warn but allow
        /// if the device can't do 4X at all (so the monitor goes deaf during a 4X read it can't see).
        /// </summary>
        private bool CheckVpwFourXGate()
        {
            if (!this.Vehicle.Supports4X)
            {
                this.AddUserMessage("Bus monitor: this device can't switch to 4X, so 4X reads will not be captured.");
                return true;
            }

            if (!this.Vehicle.Enable4xReadWrite)
            {
                MessageBox.Show(
                    this,
                    "Bus monitoring needs 4X enabled so it can follow a 4X read.\n\n" +
                    "Turn on 'Enable 4X' in the Select Device dialog, then reconnect.",
                    "Bus Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private IReadOnlyCollection<uint>? ParseCanIds()
        {
            List<uint> ids = new List<uint>();
            foreach (string token in this.monitorFilterTextBox.Text.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id))
                {
                    ids.Add(id);
                }
            }

            // Empty (or all-invalid) means accept every id.
            return ids.Count == 0 ? null : ids;
        }

        // Called from DisableUserInput: keep Start/Stop usable only while monitoring (so Stop works);
        // otherwise a normal operation is running and the monitor must not be startable.
        private void MonitorOnDisableUserInput()
        {
            if (this.busMonitorTab == null)
            {
                return;
            }

            this.monitorStartStopButton.Enabled = this.monitoring;
            if (!this.monitoring)
            {
                this.monitorVpwRadio.Enabled = false;
                this.monitorCanRadio.Enabled = false;
                this.monitorFilterTextBox.Enabled = false;
            }
        }

        private void MonitorOnEnableUserInput()
        {
            if (this.busMonitorTab == null || this.monitoring)
            {
                return;
            }

            this.RefreshMonitorCapability();
        }
    }
}
