// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// The View box on the Configuration tab: which parts of the app are on screen.
    /// </summary>
    /// <remarks>
    /// Built in code rather than in the designer, like the Dash and Monitors tabs, so
    /// MainForm.Designer stays as generated.
    ///
    /// Check boxes rather than radio buttons: a radio group allows one choice, and these are
    /// independent toggles that are mostly all on at once.
    /// </remarks>
    partial class MainForm
    {
        // The tab is four stacked boxes. Laid out from these rather than from numbers written into
        // each one, so inserting a row means changing a height and not every box below it.
        private const int BoxLeft = 4;
        private const int BoxGap = 8;

        /// <summary>Narrowest the boxes go, which is what the View row of check boxes needs.</summary>
        private const int MinimumBoxWidth = 331;

        /// <summary>
        /// How wide the boxes are built. They also anchor to both edges, so this is the starting
        /// point rather than the last word - the device names beside the buttons are as long as the
        /// interface is called, and a fixed width clipped them.
        /// </summary>
        private int BoxWidth =>
            Math.Max(MinimumBoxWidth, this.configurationTab.ClientSize.Width - (BoxLeft * 2));
        private const int DevicesBoxTop = 4;
        private const int DevicesBoxHeight = 84;
        private const int PathsBoxHeight = 82;
        private const int ViewBoxHeight = 96;
        private const int OptionsBoxHeight = 48;

        private static int PathsBoxTop => DevicesBoxTop + DevicesBoxHeight + BoxGap;

        private static int ViewBoxTop => PathsBoxTop + PathsBoxHeight + BoxGap;

        private static int OptionsBoxTop => ViewBoxTop + ViewBoxHeight + BoxGap;

        private CheckBox? viewDebug;
        private CheckBox? viewDashboard;
        private CheckBox? viewMonitors;
        private CheckBox? viewPids;
        private CheckBox? viewZoom;
        private CheckBox? viewAuxiliaryBus;
        private CheckBox? viewProfiles;
        private CheckBox? autoStartLogging;
        private ComboBox? themeChoice;

        /// <summary>Set while the boxes are being restored, so nothing is saved or applied twice.</summary>
        private bool initializingViewOptions;

        /// <summary>
        /// Every tab in the order it belongs, whether or not it is currently shown. A TabControl has
        /// no per-page visibility, so hiding one means removing it and showing it again means
        /// inserting it back at the right index - which needs this list to work out where that is.
        /// </summary>
        private List<TabPage> TabOrder => new List<TabPage>
        {
            this.configurationTab,
            this.profilesTab,
            this.parametersTab,
            this.canTab,
            this.debugTab,
            this.dashboardTab,
            this.monitorsTab,
            this.troubleCodesTab,
        };

        /// <summary>Build the Configuration tab's boxes. Called from MainForm_Load, after the tabs exist.</summary>
        private void InitializeViewOptions()
        {
            this.BuildDevicesBox();
            this.BuildPathsBox();

            GroupBox box = new GroupBox
            {
                Text = "View",
                Left = BoxLeft,
                Top = ViewBoxTop,
                Width = BoxWidth,
                Height = ViewBoxHeight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            this.viewDebug = AddViewCheckBox(box, "&Debug", 8, 18);
            this.viewDashboard = AddViewCheckBox(box, "Dash&board", 72, 18);
            this.viewMonitors = AddViewCheckBox(box, "&Monitors", 152, 18);
            this.viewPids = AddViewCheckBox(box, "&PIDs", 224, 18);
            this.viewZoom = AddViewCheckBox(box, "&Zoom", 276, 18);

            // Second row, and off by default: almost nobody has an auxiliary bus, so the tab it
            // reveals stays out of the way until someone goes looking for it.
            this.viewAuxiliaryBus = AddViewCheckBox(
                box, "Auxiliary &bus (CAN sensors on a separate interface)", 8, 40);

            // Third row. Profiles are optional workspace; keep them out of the way by default.
            this.viewProfiles = AddViewCheckBox(box, "&Profiles", 8, 66);

            // A theme is not a thing being shown or hidden, but it is what the window looks like,
            // which is what this box is about.
            box.Controls.Add(new Label { Text = "&Theme", Left = 96, Top = 66, AutoSize = true });

            this.themeChoice = new ComboBox
            {
                Left = 144,
                Top = 62,
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            this.themeChoice.Items.AddRange(new object[] { "System", "Light", "Dark" });
            this.themeChoice.SelectedIndexChanged += this.ThemeChoice_SelectedIndexChanged;
            box.Controls.Add(this.themeChoice);

            this.configurationTab.Controls.Add(box);

            GroupBox options = new GroupBox
            {
                Text = "Options",
                Left = BoxLeft,
                Top = OptionsBoxTop,
                Width = BoxWidth,
                Height = OptionsBoxHeight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            this.autoStartLogging = AddViewCheckBox(options, "&Auto start logging", 8, 18);

            this.configurationTab.Controls.Add(options);

            try
            {
                this.initializingViewOptions = true;

                this.viewDebug.Checked = Configuration.Settings.ViewDebug;
                this.viewDashboard.Checked = Configuration.Settings.ViewDashboard;
                this.viewMonitors.Checked = Configuration.Settings.ViewMonitors;
                this.viewPids.Checked = Configuration.Settings.ViewPids;
                this.viewZoom.Checked = Configuration.Settings.ViewZoom;
                this.viewAuxiliaryBus.Checked = Configuration.Settings.ViewAuxiliaryBus;
                this.viewProfiles.Checked = Configuration.Settings.ViewProfiles;
                this.autoStartLogging.Checked = Configuration.Settings.AutoStartLogging;
                this.themeChoice.SelectedItem =
                    AppTheme.Parse(Configuration.Settings.Theme).ToString();
            }
            finally
            {
                this.initializingViewOptions = false;
            }

            this.ApplyViewOptions();
        }

        /// <summary>
        /// The Devices box: the interface for the PCM, and the one for the auxiliary bus.
        /// </summary>
        /// <remarks>
        /// Both pickers together, because choosing an interface is one job however many buses are
        /// involved. The auxiliary picker used to live on its own tab, where it was hidden with the
        /// tab and so could not be reached without first turning the bus on somewhere else.
        ///
        /// The designer's controls are moved in here rather than recreated, so their handlers and
        /// everything that reads them keep working.
        /// </remarks>
        private void BuildDevicesBox()
        {
            GroupBox box = new GroupBox
            {
                Text = "Devices",
                Left = BoxLeft,
                Top = DevicesBoxTop,
                Width = BoxWidth,
                Height = DevicesBoxHeight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            Reparent(this.selectButton, box, 8, 18);
            Reparent(this.deviceDescription, box, 232, 24);
            Reparent(this.selectCanButton, box, 8, 48);
            Reparent(this.canDeviceDescription, box, 232, 54);

            this.selectCanButton.Text = "Select Au&xiliary Device";
            this.selectCanButton.Width = 216;

            // Sized to the name rather than to a guess at how long a name gets. The box clips
            // whatever overruns it, which is what hid the end of the longer interface names.
            this.deviceDescription.AutoSize = true;
            this.canDeviceDescription.AutoSize = true;

            this.configurationTab.Controls.Add(box);
        }

        /// <summary>The Paths box: where logs are written, and a way to go and look at them.</summary>
        private void BuildPathsBox()
        {
            GroupBox box = new GroupBox
            {
                Text = "Paths",
                Left = BoxLeft,
                Top = PathsBoxTop,
                Width = BoxWidth,
                Height = PathsBoxHeight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            Reparent(this.setDirectory, box, 8, 18);
            Reparent(this.logFilePath, box, 120, 23);
            Reparent(this.openDirectory, box, 8, 47);

            this.configurationTab.Controls.Add(box);
        }

        /// <summary>
        /// Move a control the designer created into one of the boxes, at a position within it.
        /// </summary>
        private static void Reparent(Control control, Control parent, int left, int top)
        {
            control.Parent?.Controls.Remove(control);
            control.Left = left;
            control.Top = top;
            parent.Controls.Add(control);
        }

        /// <summary>Show or hide the auxiliary picker, which follows the View toggle.</summary>
        private void ApplyAuxiliaryDeviceVisibility()
        {
            bool enabled = this.AuxiliaryBusEnabled;

            if (this.selectCanButton != null)
            {
                this.selectCanButton.Visible = enabled;
            }

            if (this.canDeviceDescription != null)
            {
                this.canDeviceDescription.Visible = enabled;
            }
        }

        private void ThemeChoice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this.initializingViewOptions)
            {
                return;
            }

            Configuration.Settings.Theme = this.themeChoice?.SelectedItem?.ToString() ?? "System";
            Configuration.Save(this);

            this.ApplyTheme();
        }

        /// <summary>Repaint the whole window in the chosen theme.</summary>
        private void ApplyTheme()
        {
            AppTheme.Select(AppTheme.Parse(Configuration.Settings.Theme));
            AppTheme.Apply(this);

            // The dashboard's locked rows carry their own colours, so they are the one part of the
            // grid that Apply cannot reach.
            this.ApplyDashboardLocks();

            // The dash and the monitors draw themselves and cache nothing, so a repaint is all they
            // need to pick the new palette up.
            this.Invalidate(true);
        }

        /// <summary>Whether connecting an interface should begin reading the vehicle by itself.</summary>
        private bool AutoStartEnabled => this.autoStartLogging?.Checked ?? true;

        private CheckBox AddViewCheckBox(GroupBox box, string text, int left, int top)
        {
            CheckBox check = new CheckBox
            {
                Text = text,
                Left = left,
                Top = top,
                AutoSize = true,
            };

            check.CheckedChanged += this.ViewOption_CheckedChanged;
            box.Controls.Add(check);
            return check;
        }

        private void ViewOption_CheckedChanged(object? sender, EventArgs e)
        {
            if (this.initializingViewOptions)
            {
                return;
            }

            Configuration.Settings.ViewDebug = this.viewDebug?.Checked ?? true;
            Configuration.Settings.ViewDashboard = this.viewDashboard?.Checked ?? true;
            Configuration.Settings.ViewMonitors = this.viewMonitors?.Checked ?? true;
            Configuration.Settings.ViewPids = this.viewPids?.Checked ?? true;
            Configuration.Settings.ViewZoom = this.viewZoom?.Checked ?? false;
            Configuration.Settings.ViewAuxiliaryBus = this.viewAuxiliaryBus?.Checked ?? false;
            Configuration.Settings.ViewProfiles = this.viewProfiles?.Checked ?? false;
            Configuration.Settings.AutoStartLogging = this.autoStartLogging?.Checked ?? true;
            Configuration.Save(this);

            this.ApplyViewOptions();

            // The auxiliary bus toggle is also its enable, so rebuild the logger only when that
            // setting changes. Other view settings (including Auto Start Logging) must not stop
            // and reconnect a live J2534 monitor as a side effect.
            if (ReferenceEquals(sender, this.viewAuxiliaryBus))
            {
                this.ResetProfile();
                this.CreateProfileFromGrid();
            }
        }

        /// <summary>
        /// Show or hide the tabs and the two right-hand panes.
        /// </summary>
        /// <remarks>
        /// Debug, Dashboard and Monitors are tabs. PIDs and Zoom are the two panes stacked on the
        /// right of every page - the live values list and the big-text zoom display - which share one
        /// splitter, so they are handled together: with neither wanted, the whole right side goes
        /// rather than leaving an empty splitter behind.
        /// </remarks>
        private void ApplyViewOptions()
        {
            this.SetTabShown(this.debugTab, this.viewDebug?.Checked ?? true);
            this.SetTabShown(this.dashboardTab, this.viewDashboard?.Checked ?? true);
            this.SetTabShown(this.monitorsTab, this.viewMonitors?.Checked ?? true);
            this.SetTabShown(this.canTab, this.AuxiliaryBusEnabled);
            this.SetTabShown(this.profilesTab, this.viewProfiles?.Checked ?? false);
            this.ApplyAuxiliaryDeviceVisibility();

            bool pids = this.PidsVisible;
            bool zoom = this.ZoomVisible;

            if (!pids && !zoom)
            {
                this.splitContainer1.Panel2Collapsed = true;
                return;
            }

            // A quarter of the window per pane, once, at startup.
            this.SetInitialRightPaneWidth(pids, zoom);

            // Widen the right side first if necessary. A SplitContainer refuses to show a panel that
            // its width cannot satisfy, which is why toggling Zoom on did nothing once the values
            // pane had been narrowed to fit its text - 200 + 200 + splitter would not fit.
            this.EnsureRightPaneWidth(pids, zoom);

            this.splitContainer1.Panel2Collapsed = false;

            // Collapse one at a time: a SplitContainer cannot have both panels collapsed, and the
            // branch above has already handled the case where neither is wanted.
            this.splitContainer2.Panel1Collapsed = !pids;
            this.splitContainer2.Panel2Collapsed = !zoom;

            // With zoom gone, the values list should shrink back to what its text needs instead of
            // keeping the space zoom was using.
            this.RefitValuesPaneSoon();
        }

        /// <summary>Whether the startup width has been applied, since it is wanted only once.</summary>
        private bool initialRightPaneWidthSet;

        /// <summary>
        /// Give the right-hand side a quarter of the window for each pane it is showing.
        /// </summary>
        /// <remarks>
        /// The designer's split puts 58% of the window on the right, which is a lot of room for a
        /// column of numbers. <see cref="FitValuesPane"/> already narrows the list to its text, but
        /// it cannot measure text that does not exist yet, so nothing moved until logging started.
        /// This is the starting point; that still refines it once values arrive.
        ///
        /// Startup only. Afterwards the splitter is the user's, and toggling a pane on asks for the
        /// minimum it needs rather than taking a quarter back.
        /// </remarks>
        private void SetInitialRightPaneWidth(bool pids, bool zoom)
        {
            int panes = (pids ? 1 : 0) + (zoom ? 1 : 0);
            if (this.initialRightPaneWidthSet || panes == 0 || this.splitContainer1.Width <= 0)
            {
                return;
            }

            int available = this.splitContainer1.Width - this.splitContainer1.SplitterWidth;
            int lowest = this.splitContainer1.Panel1MinSize;
            int highest = available - this.splitContainer1.Panel2MinSize;
            if (highest < lowest)
            {
                return;
            }

            int distance = available - (available * panes / 4);
            this.splitContainer1.SplitterDistance = Math.Max(lowest, Math.Min(distance, highest));
            this.initialRightPaneWidthSet = true;
        }

        /// <summary>
        /// Make sure the right-hand side is wide enough for the panes being shown, since
        /// SplitterDistance silently refuses a value that breaks a panel's minimum.
        /// </summary>
        private void EnsureRightPaneWidth(bool pids, bool zoom)
        {
            int required = 0;
            if (pids)
            {
                required += this.splitContainer2.Panel1MinSize;
            }

            if (zoom)
            {
                required += this.splitContainer2.Panel2MinSize;
            }

            if (pids && zoom)
            {
                required += this.splitContainer2.SplitterWidth;
            }

            int available = this.splitContainer1.Width - this.splitContainer1.SplitterWidth;
            int current = available - this.splitContainer1.SplitterDistance;
            if (current >= required || available <= 0)
            {
                return;
            }

            int distance = available - required;
            int lowest = this.splitContainer1.Panel1MinSize;
            int highest = available - this.splitContainer1.Panel2MinSize;
            if (highest < lowest)
            {
                return;
            }

            this.splitContainer1.SplitterDistance = Math.Max(lowest, Math.Min(distance, highest));
        }

        /// <summary>Whether the Zoom pane is on screen, so nothing paints on it when it is not.</summary>
        private bool ZoomVisible => this.viewZoom?.Checked ?? false;

        /// <summary>Whether the live PID values pane is on screen.</summary>
        private bool PidsVisible => this.viewPids?.Checked ?? true;

        /// <summary>
        /// Whether an auxiliary bus is in use. One toggle does both jobs: it reveals the tab and it
        /// enables the logging, because a hidden tab the logger was still reading from would be a
        /// thing running with nowhere to say so.
        /// </summary>
        private bool AuxiliaryBusEnabled => this.viewAuxiliaryBus?.Checked ?? false;

        private void SetTabShown(TabPage page, bool shown)
        {
            if (page == null)
            {
                return;
            }

            bool present = this.tabs.TabPages.Contains(page);
            if (present == shown)
            {
                return;
            }

            if (!shown)
            {
                this.tabs.TabPages.Remove(page);
                return;
            }

            // Insert where it belongs rather than appending, so turning a tab off and on again does
            // not shuffle the row.
            List<TabPage> order = this.TabOrder;
            int target = order.IndexOf(page);
            int index = 0;
            for (int position = 0; position < target; position++)
            {
                if (this.tabs.TabPages.Contains(order[position]))
                {
                    index++;
                }
            }

            this.tabs.TabPages.Insert(Math.Min(index, this.tabs.TabPages.Count), page);
        }
    }
}
