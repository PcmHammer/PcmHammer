// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// One screen update's worth of data, handed from the logging thread to the UI.
    /// </summary>
    internal class DisplayFrame
    {
        public DisplayFrame(string text, List<ZoomedParameter> zoomed, IReadOnlyList<LogRowElement> elements)
        {
            this.Text = text;
            this.Zoomed = zoomed;
            this.Elements = elements;
        }

        public string Text { get; }

        public List<ZoomedParameter> Zoomed { get; }

        public IReadOnlyList<LogRowElement> Elements { get; }
    }

    /// <summary>
    /// The Dash and Monitors tabs: importing a TunerPro ADX, binding its gauges to parameters this
    /// logger can poll, and saving the result as a .plz package.
    /// </summary>
    /// <remarks>
    /// Built in code rather than in the designer - the tab contents are a handful of controls that
    /// read better here than in generated output, and MainForm.Designer is left alone. The
    /// dashboardTab had been sitting in the designer unused, so it is adopted rather than recreated.
    /// </remarks>
    partial class MainForm
    {
        /// <summary>
        /// How often the screen is refreshed. The logger runs as fast as the bus and the PCM allow;
        /// ten times a second is as fast as a gauge is worth reading, and holding the display to it
        /// means a slow machine drops frames instead of dragging logging down with it.
        /// </summary>
        private const int DisplayRefreshIntervalMs = 100;

        private const string DashboardFileFilter =
            "PcmLogger dashboards (*.plz)|*.plz|TunerPro definitions (*.adx)|*.adx|All files (*.*)|*.*";

        private readonly TabPage monitorsTab = new TabPage { Text = "Monitors", UseVisualStyleBackColor = true };

        private DashboardView? dashboardView;
        private ComboBox? dashboardSelector;
        private Label? dashboardStatus;
        private Button? saveDashboardButton;

        private Label? monitorStatus;
        private TableLayoutPanel? monitorPanes;

        /// <summary>Follow the newest sample, or stop and hold still.</summary>
        private Button? monitorPlayButton;

        private const string PlayGlyph = "▶";
        private const string StopGlyph = "■";

        /// <summary>The editable time of the cursor, in minutes and seconds from the log's start.</summary>
        private TextBox? monitorTime;

        /// <summary>Set while the panes are being brought into line, so the sync does not echo.</summary>
        private bool syncingMonitorCursor;

        private bool syncingMonitorSpan;

        private readonly List<MonitorView> monitorViews = new List<MonitorView>();

        /// <summary>
        /// Every sample of this session, which the monitors draw from. Replaced when the profile
        /// changes, because the columns change with it.
        /// </summary>
        /// <remarks>
        /// The buffer between the logging thread and the screen. The logger appends at bus speed and
        /// the UI reads a window of it ten times a second, so neither waits for the other and a
        /// redraw that is skipped loses nothing.
        /// </remarks>
        private LogHistory? history;

        /// <summary>Parameter id to display name for everything being logged, for the trace picker.</summary>
        private Dictionary<string, string> loggedParameters =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Parameter id to units. History stores only numbers, so this is what lets a row rebuilt
        /// from it carry the same labels a live row did.
        /// </summary>
        private Dictionary<string, string> loggedUnits =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private LoggerPackage? dashboardPackage;
        private string? dashboardPath;

        /// <summary>Set while the selector is being refilled, so its events do no work mid-rebuild.</summary>
        private bool applyingPackage;

        /// <summary>
        /// The logging thread publishes here and never waits; the timer below collects whatever is
        /// current. See LatestValueFeed for why this is a single slot rather than a queue.
        /// </summary>
        private readonly LatestValueFeed<DisplayFrame> displayFeed = new LatestValueFeed<DisplayFrame>();

        private Timer? displayTimer;

        /// <summary>Add the two tabs. Called from MainForm_Load once the designer's controls exist.</summary>
        private void InitializeDashboardTabs()
        {
            this.BuildDashboardTab();
            this.BuildMonitorsTab();
            this.BuildTroubleCodesTab();

            this.tabs.TabPages.Add(this.dashboardTab);
            this.tabs.TabPages.Add(this.monitorsTab);
            this.tabs.TabPages.Add(this.troubleCodesTab);

            this.displayTimer = new Timer { Interval = DisplayRefreshIntervalMs };
            this.displayTimer.Tick += this.DisplayTimer_Tick;
            this.displayTimer.Start();

            this.LoadLastDashboard();
        }

        /// <summary>
        /// Paint whatever the logger has published since the last tick. Nothing new means nothing to
        /// redraw, and several new rows means only the most recent is drawn.
        /// </summary>
        private void DisplayTimer_Tick(object? sender, EventArgs e)
        {
            // A WinForms Timer keeps firing while the form tears down, and the panels it draws on are
            // already gone by then - DrawZoomedParameters calls CreateGraphics on one, which throws
            // ObjectDisposedException and takes the process with it. Stopping the timer on close is
            // not enough on its own: a tick already queued still arrives after that.
            if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
            {
                return;
            }

            // Before the frame check: the count has to keep moving even on a tick where no new row
            // arrived, or it would stall whenever the PCM is slower than the display.
            if (this.saving)
            {
                this.recordingStatus.Text =
                    "Recording frame " +
                    System.Threading.Volatile.Read(ref this.recordedFrames).ToString("N0");
            }

            DisplayFrame? frame = this.displayFeed.TakeLatest();
            if (frame == null)
            {
                return;
            }

            this.logValues.Text = frame.Text;

            // Refit once per profile change, here rather than at the change itself: the pane is
            // sized to the text it will actually hold, and that text does not exist until a row has
            // been logged.
            if (this.refitValuesPane)
            {
                this.refitValuesPane = false;
                this.FitValuesPane();
            }

            // Drawing on a collapsed splitter panel is wasted work at best; CreateGraphics on one
            // with no width is a fault at worst.
            if (this.ZoomVisible)
            {
                this.DrawZoomedParameters(frame.Zoomed);
            }

            // Unless the cursor is parked on a sample, in which case the gauges belong to it: a live
            // frame arriving would otherwise drag them off the moment being inspected.
            if (this.PrimaryMonitor?.CursorSample is int cursor && cursor >= 0)
            {
                this.ShowCursorOnDashboard(cursor);
            }
            else
            {
                this.dashboardView?.SetValues(frame.Elements);
            }

            // What the logger is producing, for the monitors' trace picker. Rebuilt only when the
            // set of columns changes, which is when the profile changes.
            if (this.loggedParameters.Count != frame.Elements.Count)
            {
                this.loggedParameters = frame.Elements
                    .GroupBy(e => e.ParameterId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().ParameterName, StringComparer.OrdinalIgnoreCase);

                this.loggedUnits = frame.Elements
                    .GroupBy(e => e.ParameterId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Units, StringComparer.OrdinalIgnoreCase);
            }

            // A disconnect can happen before the first queued sample reaches the writer. Keep the
            // clear action in step when that final sample arrives after the button state was set.
            this.UpdateClearLogButton();

            // Monitors read from history rather than from this frame, so they are redrawn on the
            // same tick but are not fed by it. Only the visible tab is worth repainting.
            if (this.tabs.SelectedTab == this.monitorsTab)
            {
                foreach (MonitorView view in this.monitorViews)
                {
                    view.AvailableParameters = this.loggedParameters;

                    // The logging thread replaces the history when the profile changes; pick that up
                    // here rather than having it reach across threads to tell us.
                    if (!ReferenceEquals(view.History, this.history))
                    {
                        view.History = this.history;
                        view.CursorSample = -1;
                    }

                    view.Invalidate();
                }
            }
        }

        /// <summary>
        /// Record one logged row. Called on the writer thread; the history is built from the first
        /// row of a session because that is what states the columns, and the row carries math and CAN
        /// values the profile alone does not mention.
        /// </summary>
        private void AppendToHistory(IReadOnlyList<LogRowElement> row)
        {
            if (row.Count == 0)
            {
                return;
            }

            LogHistory? current = this.history;
            if (current == null || current.ColumnCount != row.Count)
            {
                // Published, not marshalled. Calling BeginInvoke from here raced the form's teardown:
                // it forces handle creation, and arriving mid-Dispose throws "Dispose() cannot be
                // called while doing CreateHandle()". The display timer picks the new history up on
                // its next tick instead, which is already on the UI thread and already bounded.
                current = new LogHistory(row.Select(e => e.ParameterId));
                this.history = current;
                this.historyLoadedFromFile = false;
            }

            current.Append(DateTime.Now, row);
        }

        /// <summary>Set when the selected parameters change, so the values pane is refitted.</summary>
        private bool refitValuesPane = true;

        /// <summary>Request a refit of the values pane, because what it has to show has changed.</summary>
        private void RefitValuesPaneSoon()
        {
            this.refitValuesPane = true;
        }

        /// <summary>
        /// Narrow the values pane to what its text actually needs, giving the rest back to the tabs.
        /// </summary>
        /// <remarks>
        /// The designer splits the window roughly in half, which suited the zoom panel sharing that
        /// side. With zoom hidden, half the window for a column of numbers leaves the dashboard and
        /// the parameter grid cramped for no reason.
        ///
        /// Only when zoom is hidden: with it shown, the pane is shared and sizing it to the text
        /// would squeeze the zoom display to nothing.
        /// </remarks>
        private void FitValuesPane()
        {
            if (this.ZoomVisible || !this.PidsVisible
                || this.splitContainer1.Width <= 0 || this.logValues.Lines.Length == 0)
            {
                return;
            }

            int widest = 0;
            foreach (string line in this.logValues.Lines)
            {
                Size measured = TextRenderer.MeasureText(
                    line,
                    this.logValues.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.ExpandTabs);

                widest = Math.Max(widest, measured.Width);
            }

            if (widest == 0)
            {
                return;
            }

            // Never below the pane's own minimum, which the splitter would refuse anyway.
            int wanted = Math.Max(
                this.splitContainer2.Panel1MinSize,
                widest + SystemInformation.VerticalScrollBarWidth + 16);
            int available = this.splitContainer1.Width - this.splitContainer1.SplitterWidth;
            int distance = available - wanted;

            // Never past either panel's minimum, or SplitterDistance throws.
            int lowest = this.splitContainer1.Panel1MinSize;
            int highest = available - this.splitContainer1.Panel2MinSize;
            if (highest < lowest)
            {
                return;
            }

            this.splitContainer1.SplitterDistance = Math.Max(lowest, Math.Min(distance, highest));
        }

        /// <summary>Stop drawing. Called from FormClosing, before the window and its panels go away.</summary>
        private void ShutdownDashboardTabs()
        {
            if (this.displayTimer != null)
            {
                this.displayTimer.Stop();
                this.displayTimer.Tick -= this.DisplayTimer_Tick;
                this.displayTimer.Dispose();
                this.displayTimer = null;
            }
        }

        private void BuildDashboardTab()
        {
            this.dashboardTab.Controls.Clear();

            Panel top = new Panel { Dock = DockStyle.Top, Height = 30 };

            Button open = new Button { Text = "Open / Import...", Left = 3, Top = 3, Width = 110 };
            open.Click += this.OpenDashboard_Click;

            this.saveDashboardButton = new Button { Text = "Save As...", Left = 117, Top = 3, Width = 80, Enabled = false };
            this.saveDashboardButton.Click += this.SaveDashboard_Click;

            Button restore = new Button { Text = "Default", Left = 201, Top = 3, Width = 70 };
            restore.Click += this.RestoreDefaultDashboard_Click;

            this.dashboardSelector = new ComboBox
            {
                Left = 275,
                Top = 4,
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            this.dashboardSelector.SelectedIndexChanged += (s, e) => this.ShowSelectedDashboard();

            this.dashboardStatus = new Label { Left = 482, Top = 8, AutoSize = true, Text = "No dashboard loaded." };

            top.Controls.Add(open);
            top.Controls.Add(this.saveDashboardButton);
            top.Controls.Add(restore);
            top.Controls.Add(this.dashboardSelector);
            top.Controls.Add(this.dashboardStatus);

            this.dashboardView = new DashboardView { Dock = DockStyle.Fill };
            this.dashboardView.GaugeEditRequested += this.Gauge_EditRequested;
            this.dashboardView.GaugeBindingChanged += this.Gauge_BindingChanged;

            // Fill is added before Top so docking leaves the bar above the gauges.
            this.dashboardTab.Controls.Add(this.dashboardView);
            this.dashboardTab.Controls.Add(top);
        }

        private void BuildMonitorsTab()
        {
            this.monitorsTab.Controls.Clear();

            Panel top = new Panel { Dock = DockStyle.Top, Height = 30 };

            // Navigation only. Play/Stop and Record used to live here, duplicating the toolbar and
            // meaning something different from it; what this page needs is a way to walk the log.
            Button backFar = MonitorNavButton("<<", 3, (s, e) => this.StepMonitorCursorSeconds(-2));
            Button back = MonitorNavButton("<", 41, (s, e) => this.StepMonitorCursor(-1));

            // Glyphs rather than words, in the middle where a tape deck puts them: this row is five
            // buttons wide and "Play"/"Stop" spelled out would crowd out the time box.
            this.monitorPlayButton = MonitorNavButton(PlayGlyph, 79, this.MonitorPlay_Click);

            Button forward = MonitorNavButton(">", 117, (s, e) => this.StepMonitorCursor(1));
            Button forwardFar = MonitorNavButton(">>", 155, (s, e) => this.StepMonitorCursorSeconds(2));

            this.monitorTime = new TextBox { Left = 197, Top = 4, Width = 70, Text = string.Empty };
            this.monitorTime.KeyDown += this.MonitorTime_KeyDown;
            this.monitorTime.Leave += this.MonitorTime_Leave;

            this.monitorStatus = new Label { Left = 275, Top = 8, AutoSize = true, Text = "No dashboard loaded." };

            top.Controls.Add(backFar);
            top.Controls.Add(back);
            top.Controls.Add(this.monitorPlayButton);
            top.Controls.Add(forward);
            top.Controls.Add(forwardFar);
            top.Controls.Add(this.monitorTime);
            top.Controls.Add(this.monitorStatus);

            this.UpdateMonitorPlayButton();

            // One pane per monitor, stacked. Equal rows are added as the package is applied.
            this.monitorPanes = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 0,
            };
            this.monitorPanes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            this.monitorsTab.Controls.Add(this.monitorPanes);
            this.monitorsTab.Controls.Add(top);
        }

        /// <summary>
        /// Build a pane per monitor the package says is visible, each with its own pan scrollbar.
        /// </summary>
        private void BuildMonitorPanes()
        {
            if (this.monitorPanes == null)
            {
                return;
            }

            foreach (MonitorView existing in this.monitorViews)
            {
                existing.SeriesVisibilityChanged -= this.MonitorView_SeriesVisibilityChanged;
                existing.CursorMoved -= this.MonitorView_CursorMoved;
                existing.SpanChanged -= this.MonitorView_SpanChanged;
            }

            this.monitorViews.Clear();
            this.monitorPanes.SuspendLayout();
            this.monitorPanes.Controls.Clear();
            this.monitorPanes.RowStyles.Clear();
            this.monitorPanes.RowCount = 0;

            List<MonitorLayout> monitors = this.dashboardPackage == null
                ? new List<MonitorLayout>()
                : this.dashboardPackage.Monitors.Where(m => m.Visible).OrderBy(m => m.Order).ToList();

            foreach (MonitorLayout monitor in monitors)
            {
                Panel host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };

                MonitorView view = new MonitorView
                {
                    Dock = DockStyle.Fill,
                    MonitorLayout = monitor,
                    History = this.history,

                    // So a pane rebuilt after logging has stopped still offers the trace picker.
                    AvailableParameters = this.loggedParameters,
                };

                view.SeriesVisibilityChanged += this.MonitorView_SeriesVisibilityChanged;
                view.CursorMoved += this.MonitorView_CursorMoved;
                view.SpanChanged += this.MonitorView_SpanChanged;

                host.Controls.Add(view);

                this.monitorPanes.RowCount++;
                this.monitorPanes.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / Math.Max(1, monitors.Count)));
                this.monitorPanes.Controls.Add(host, 0, this.monitorPanes.RowCount - 1);

                this.monitorViews.Add(view);
            }

            this.monitorPanes.ResumeLayout();
        }

        private static Button MonitorNavButton(string text, int left, EventHandler onClick)
        {
            Button button = new Button { Text = text, Left = left, Top = 3, Width = 34, Height = 23 };
            button.Click += onClick;
            return button;
        }

        /// <summary>
        /// Follow the newest sample again, or stop following and hold the view still.
        /// </summary>
        /// <remarks>
        /// One button for every pane: they are views of one session, and letting them run and stop
        /// separately only raises the question of which one is live. Playing clears the cursor,
        /// because a cursor pinned to an old sample is the thing that stopped the view following.
        /// </remarks>
        private void MonitorPlay_Click(object? sender, EventArgs e)
        {
            bool stop = this.monitorViews.Any(v => !v.Paused);

            foreach (MonitorView view in this.monitorViews)
            {
                view.Paused = stop;

                if (!stop)
                {
                    view.CursorSample = -1;
                }

                view.Invalidate();
            }

            this.UpdateMonitorTimeBox();
            this.UpdateMonitorPlayButton();
        }

        private void UpdateMonitorPlayButton()
        {
            if (this.monitorPlayButton == null)
            {
                return;
            }

            bool following = this.monitorViews.Any(v => !v.Paused);
            this.monitorPlayButton.Text = following ? StopGlyph : PlayGlyph;
        }

        /// <summary>
        /// One pane's cursor moved, so put every other pane on the same sample.
        /// </summary>
        /// <remarks>
        /// The same sample rather than the same time: the panes share one history, so an index is
        /// the same moment in all of them and needs no matching up.
        /// </remarks>
        private void MonitorView_CursorMoved(object? sender, EventArgs e)
        {
            if (this.syncingMonitorCursor || !(sender is MonitorView source))
            {
                return;
            }

            this.SyncMonitorCursor(source.CursorSample, source);
        }

        /// <summary>
        /// One pane was zoomed, so give every other pane the same time window.
        /// </summary>
        /// <remarks>
        /// Two monitors are read against each other - what the trims did when the throttle moved -
        /// and that only works while both cover the same span. Zooming the one under the pointer
        /// alone would silently put them on different scales.
        /// </remarks>
        private void MonitorView_SpanChanged(object? sender, EventArgs e)
        {
            if (this.syncingMonitorSpan || !(sender is MonitorView source))
            {
                return;
            }

            try
            {
                this.syncingMonitorSpan = true;

                foreach (MonitorView view in this.monitorViews)
                {
                    if (!ReferenceEquals(view, source))
                    {
                        view.SetSpanSeconds(source.SpanSeconds);
                    }
                }
            }
            finally
            {
                this.syncingMonitorSpan = false;
            }
        }

        private void SyncMonitorCursor(int sample, MonitorView? except)
        {
            try
            {
                this.syncingMonitorCursor = true;

                foreach (MonitorView view in this.monitorViews)
                {
                    if (view != except)
                    {
                        view.ShowCursorAt(sample);
                    }
                }
            }
            finally
            {
                this.syncingMonitorCursor = false;
            }

            this.UpdateMonitorTimeBox();
            this.UpdateMonitorPlayButton();
            this.ShowCursorOnDashboard(sample);
        }

        /// <summary>
        /// Put the gauges on the sample the cursor is sitting on.
        /// </summary>
        /// <remarks>
        /// So that scrubbing a log drives the whole app, not just the plots. The controls for it are
        /// on the Monitors page, which is awkward when the gauges are on another tab - but a cursor
        /// that moved the plots and left the gauges reading something else would be worse.
        ///
        /// Rebuilt from history rather than replayed from the logger's own rows: history is the only
        /// thing that holds more than the newest sample, and it is what a loaded log fills.
        /// </remarks>
        private void ShowCursorOnDashboard(int sample)
        {
            LogHistory? current = this.history;
            if (this.dashboardView == null || current == null || sample < 0 || sample >= current.Count)
            {
                return;
            }

            LogRowElement[] row = new LogRowElement[current.ColumnCount];
            for (int column = 0; column < current.ColumnCount; column++)
            {
                string id = current.ColumnIds[column];
                double value = current.ValueOrNaN(sample, column);

                this.loggedParameters.TryGetValue(id, out string? name);
                this.loggedUnits.TryGetValue(id, out string? units);

                row[column] = new LogRowElement(
                    id,
                    name ?? id,
                    units ?? string.Empty,
                    double.IsNaN(value) ? string.Empty : value.ToString("0.###"),
                    double.IsNaN(value) ? 0 : value);
            }

            this.dashboardView.SetValues(row);
        }

        /// <summary>
        /// The pane the cursor is read from. Any will do - they are kept in step - but a pane that
        /// has a cursor is preferred, so the first step after a load starts somewhere sensible.
        /// </summary>
        private MonitorView? PrimaryMonitor =>
            this.monitorViews.FirstOrDefault(v => v.CursorSample >= 0) ?? this.monitorViews.FirstOrDefault();

        private void StepMonitorCursor(int samples)
        {
            MonitorView? primary = this.PrimaryMonitor;
            LogHistory? current = this.history;
            if (primary == null || current == null || current.Count == 0)
            {
                return;
            }

            // With no cursor yet, start at the newest sample: that is what is on screen.
            int from = primary.CursorSample >= 0 ? primary.CursorSample : current.Count - 1;
            this.MoveMonitorCursorTo(from + samples);
        }

        private void StepMonitorCursorSeconds(double seconds)
        {
            MonitorView? primary = this.PrimaryMonitor;
            LogHistory? current = this.history;
            if (primary == null || current == null || current.Count == 0)
            {
                return;
            }

            int from = primary.CursorSample >= 0 ? primary.CursorSample : current.Count - 1;

            // Stepped by time rather than by a sample count worked out from the average rate:
            // logging is not perfectly regular, and two seconds should mean two seconds.
            DateTime target = current.TimestampOrDefault(from).AddSeconds(seconds);
            int sample = current.IndexAtOrBefore(target);

            // IndexAtOrBefore lands on or before the target, which going forwards can leave the
            // cursor where it started when samples are sparse.
            if (seconds > 0 && sample <= from)
            {
                sample = from + 1;
            }

            this.MoveMonitorCursorTo(sample);
        }

        private void MoveMonitorCursorTo(int sample)
        {
            LogHistory? current = this.history;
            if (current == null || current.Count == 0)
            {
                return;
            }

            int clamped = Math.Max(0, Math.Min(current.Count - 1, sample));
            this.SyncMonitorCursor(clamped, null);
        }

        private void UpdateMonitorTimeBox()
        {
            if (this.monitorTime == null || this.monitorTime.Focused)
            {
                // Not while it is being typed in.
                return;
            }

            TimeSpan? elapsed = this.PrimaryMonitor?.CursorElapsed;
            this.monitorTime.Text = elapsed.HasValue ? FormatCursorTime(elapsed.Value) : string.Empty;
        }

        private static string FormatCursorTime(TimeSpan elapsed) =>
            ((int)elapsed.TotalMinutes).ToString("0") + ":" +
            elapsed.Seconds.ToString("00") + "." +
            (elapsed.Milliseconds / 100).ToString("0");

        /// <summary>
        /// Read a typed time: "1:23.4", "1:23", or a plain number of seconds.
        /// </summary>
        private static bool TryParseCursorTime(string text, out TimeSpan elapsed)
        {
            elapsed = TimeSpan.Zero;

            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            int colon = trimmed.IndexOf(':');
            if (colon < 0)
            {
                if (!double.TryParse(
                        trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out double onlySeconds) ||
                    onlySeconds < 0)
                {
                    return false;
                }

                elapsed = TimeSpan.FromSeconds(onlySeconds);
                return true;
            }

            if (!int.TryParse(trimmed.Substring(0, colon).Trim(), out int minutes) || minutes < 0)
            {
                return false;
            }

            if (!double.TryParse(
                    trimmed.Substring(colon + 1).Trim(),
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out double seconds) ||
                seconds < 0 || seconds >= 60)
            {
                return false;
            }

            elapsed = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        private void MonitorTime_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            this.ApplyTypedCursorTime();
        }

        private void MonitorTime_Leave(object? sender, EventArgs e)
        {
            this.ApplyTypedCursorTime();
        }

        /// <summary>
        /// Jump to the typed time, or put the box back to where the cursor actually is.
        /// </summary>
        private void ApplyTypedCursorTime()
        {
            MonitorView? primary = this.PrimaryMonitor;

            if (this.monitorTime == null || primary == null)
            {
                return;
            }

            if (TryParseCursorTime(this.monitorTime.Text, out TimeSpan elapsed) &&
                primary.TryGetSampleAt(elapsed, out int sample))
            {
                this.MoveMonitorCursorTo(sample);
            }

            // Either way the box is rewritten from the cursor, so an unusable entry simply reverts.
            TimeSpan? at = primary.CursorElapsed;
            this.monitorTime.Text = at.HasValue ? FormatCursorTime(at.Value) : string.Empty;
        }

        /// <summary>
        /// A trace was ticked or unticked. Which traces are shown decides what is polled, so the
        /// selection is redone - that is the same rule the dashboard follows.
        /// </summary>
        private void MonitorView_SeriesVisibilityChanged(object? sender, EventArgs e)
        {
            this.SelectDashboardParameters(replaceSelection: false);
            this.ApplyDashboardLocks();
            this.UpdateMonitorStatus();
        }

        /// <summary>
        /// Everything this logger could poll for the connected PCM, as binding candidates.
        /// </summary>
        /// <remarks>
        /// Taken from the parameter database rather than from a log row, so a dashboard can be bound
        /// before logging has ever started. Each conversion is offered separately because the units
        /// are part of the binding: a gauge drawn in kPa must not be fed volts.
        /// </remarks>
        private IReadOnlyList<BindingCandidate> BindingCandidates()
        {
            List<BindingCandidate> candidates = new List<BindingCandidate>();
            if (this.database == null)
            {
                return candidates;
            }

            try
            {
                foreach (Parameter parameter in this.database.ListParametersBySupportedOs(this.osid))
                {
                    Conversion? conversion = parameter.Conversions?.FirstOrDefault();
                    candidates.Add(new BindingCandidate(parameter.Id, parameter.Name, conversion?.Units ?? string.Empty));
                }
            }
            catch (Exception exception)
            {
                this.AddDebugMessage("Unable to list parameters for binding: " + exception.ToString());
            }

            return candidates;
        }

        /// <summary>
        /// Hook up whatever gauges and traces are still unbound. Returns how many gauges were newly
        /// bound.
        /// </summary>
        /// <remarks>
        /// This has to be able to run more than once, which is what was missing. The parameter
        /// database only exists once a PCM has been identified, so a dashboard imported or reopened
        /// before connecting binds nothing - and a package saved in that state carries no PID
        /// references at all, which no amount of reloading or switching dashboards can repair,
        /// because there is nothing in the file to select from. Re-running when the database appears
        /// fixes it without the user doing anything.
        ///
        /// Idempotent: PidBinder only considers gauges that are not already bound, so a binding made
        /// by hand is never overwritten.
        /// </remarks>
        private int BindLoadedPackage()
        {
            if (this.dashboardPackage == null)
            {
                return 0;
            }

            bool anythingUnbound = this.dashboardPackage.UnboundGaugeCount > 0
                || this.dashboardPackage.Monitors.SelectMany(m => m.Series).Any(s => !s.IsBound);

            if (!anythingUnbound)
            {
                return 0;
            }

            IReadOnlyList<BindingCandidate> candidates = this.BindingCandidates();
            if (candidates.Count == 0)
            {
                // No PCM identified yet, so there is nothing to bind to. ReapplyDashboardSelection
                // calls this again once the grid - and the database behind it - exists.
                this.AddDebugMessage("Dashboard gauges are unbound; waiting for a PCM to be identified.");
                return 0;
            }

            int bound = PidBinder.BindPackage(this.dashboardPackage, candidates);
            if (bound > 0)
            {
                this.AddUserMessage($"Hooked up {bound} gauges to parameters automatically.");
                this.UpdateDashboardStatus();
                this.UpdateMonitorList();
            }

            return bound;
        }

        private void OpenDashboard_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = DashboardFileFilter;
                dialog.Title = "Open a dashboard, or import one from TunerPro";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                this.LoadDashboard(dialog.FileName, announce: true);
            }
        }

        /// <summary>
        /// Open a .plz, or import a .adx and bind it. Returns true if something was loaded.
        /// </summary>
        private bool LoadDashboard(string path, bool announce)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), PlzFormat.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    this.dashboardPackage = PlzFormat.Load(path);
                    this.dashboardPath = path;

                    if (announce)
                    {
                        IEnumerable<string> dangling = this.dashboardPackage.DanglingPidReferences();
                        if (dangling.Any())
                        {
                            this.AddUserMessage(
                                "This dashboard refers to PIDs it does not define: " + string.Join(", ", dangling));
                        }
                    }
                }
                else
                {
                    AdxDocument document = AdxDocument.Load(path);
                    this.dashboardPackage = LoggerPackage.FromAdx(document, Path.GetFileName(path));
                    this.dashboardPackage.CreatedBy = this.GetAppNameAndVersion();
                    this.AddUserMessage("Imported " + Path.GetFileName(path) + ".");

                    // An import is not yet a saved dashboard; Save As gives it a home.
                    this.dashboardPath = null;
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage("Unable to open " + path + ": " + exception.Message);
                this.AddDebugMessage(exception.ToString());

                if (announce)
                {
                    MessageBox.Show(
                        this,
                        "Could not open that file:" + Environment.NewLine + Environment.NewLine + exception.Message,
                        "Dashboard",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return false;
            }

            this.ApplyPackage();
            return true;
        }

        private void SaveDashboard_Click(object? sender, EventArgs e)
        {
            if (this.dashboardPackage == null)
            {
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "PcmLogger dashboards (*.plz)|*.plz|All files (*.*)|*.*";
                dialog.DefaultExt = "plz";
                dialog.Title = "Save dashboard";
                dialog.FileName = this.dashboardPath != null
                    ? Path.GetFileName(this.dashboardPath)
                    : Path.GetFileNameWithoutExtension(this.dashboardPackage.Source.Name) + PlzFormat.Extension;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    // Record how this log is being collected, so the package can describe the setup
                    // it was built against, as a .phz does for a vehicle.
                    this.dashboardPackage.Communications = new PackagedCommunications
                    {
                        // The bus that was found, not an assumption: the same package works on
                        // either, and recording "VPW" for a CAN PCM would misdescribe the setup it
                        // was actually built against.
                        Protocol = this.Vehicle?.LastDetectedBus == BusProtocol.Can500k ? "CAN" : "VPW",
                        DeviceCategory = DeviceConfiguration.Settings.DeviceCategory ?? string.Empty,
                        DeviceId = DeviceConfiguration.Settings.DeviceCategory == DeviceConfiguration.Constants.DeviceCategoryJ2534
                            ? DeviceConfiguration.Settings.J2534DeviceType ?? string.Empty
                            : DeviceConfiguration.Settings.SerialPort ?? string.Empty,
                        Osid = this.osid,
                        FourXReadWrite = DeviceConfiguration.Settings.Enable4xReadWrite,
                    };

                    this.dashboardPackage.CreatedBy = this.GetAppNameAndVersion();

                    PlzFormat.Save(dialog.FileName, this.dashboardPackage);
                    this.dashboardPath = dialog.FileName;
                    this.RememberLastDashboard(dialog.FileName);

                    this.AddUserMessage("Saved dashboard to " + dialog.FileName);
                    this.UpdateDashboardStatus();
                }
                catch (Exception exception)
                {
                    this.AddUserMessage("Unable to save " + dialog.FileName + ": " + exception.Message);
                    this.AddDebugMessage(exception.ToString());
                    MessageBox.Show(
                        this,
                        "Could not save:" + Environment.NewLine + Environment.NewLine + exception.Message,
                        "Dashboard",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void RememberLastDashboard(string path)
        {
            Configuration.Settings.LastDashboard = path;
            Configuration.Save(this);
        }

        /// <summary>
        /// Reopen whatever was open last time, or fall back to the built-in dashboard.
        /// </summary>
        /// <remarks>
        /// A missing or unreadable file is not worth interrupting startup for, and now costs nothing
        /// either: there is always a dashboard, so a fresh install shows gauges rather than an empty
        /// page with no hint that gauges are what belongs there.
        /// </remarks>
        private void LoadLastDashboard()
        {
            string path = Configuration.Settings.LastDashboard;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && this.LoadDashboard(path, announce: false))
            {
                this.AddDebugMessage("Reopened dashboard " + path);
                return;
            }

            this.LoadDefaultDashboard();
        }

        /// <summary>
        /// Show the dashboard and monitors the app ships with.
        /// </summary>
        /// <remarks>
        /// The path is left unset, so this behaves like an import: it is not a file until Save As
        /// gives it one, and until then nothing can be overwritten by accident.
        /// </remarks>
        private void LoadDefaultDashboard()
        {
            this.dashboardPackage = DefaultLoggerPackage.Create();
            this.dashboardPackage.CreatedBy = this.GetAppNameAndVersion();
            this.dashboardPath = null;

            this.ApplyPackage();
        }

        /// <summary>
        /// Go back to the built-in dashboard, confirming first because it replaces what is loaded.
        /// </summary>
        private void RestoreDefaultDashboard_Click(object? sender, EventArgs e)
        {
            if (this.dashboardPackage != null)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "Replace the dashboards and monitors on screen with the built-in ones?"
                    + Environment.NewLine + Environment.NewLine
                    + "Anything not saved to a .plz file will be lost.",
                    "Dashboard",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            this.LoadDefaultDashboard();

            // Forgotten as well as replaced, so the next start does not reopen the file this was
            // just asked to step away from.
            Configuration.Settings.LastDashboard = string.Empty;
            Configuration.Save(this);

            this.AddUserMessage("Restored the built-in dashboard.");
        }

        private void ApplyPackage()
        {
            if (this.dashboardPackage == null || this.dashboardSelector == null)
            {
                return;
            }

            // Before anything reads the package: an unbound gauge cannot say which parameter it
            // needs, so nothing would be selected and nothing would display.
            this.BindLoadedPackage();

            try
            {
                // Refilling the list fires SelectedIndexChanged several times, once of them with
                // nothing selected. Re-selecting parameters on each would clear the grid and refill
                // it repeatedly; the single pass below is enough.
                this.applyingPackage = true;

                this.dashboardSelector.Items.Clear();
                foreach (DashboardLayout dashboard in this.dashboardPackage.Dashboards.OrderBy(d => d.Order))
                {
                    this.dashboardSelector.Items.Add(dashboard);
                }

                if (this.dashboardSelector.Items.Count > 0)
                {
                    // Show the one the package says is visible, falling back to the first.
                    int index = this.dashboardPackage.Dashboards
                        .OrderBy(d => d.Order)
                        .ToList()
                        .FindIndex(d => d.Visible);
                    this.dashboardSelector.SelectedIndex = index >= 0 ? index : 0;
                }
            }
            finally
            {
                this.applyingPackage = false;
            }

            if (this.saveDashboardButton != null)
            {
                this.saveDashboardButton.Enabled = true;
            }

            this.UpdateDashboardStatus();
            this.UpdateMonitorList();

            // Draws the dashboard and selects what it needs. Loading one is meant to be enough to
            // see values: a correctly bound gauge still reads nothing until the profile polls its
            // parameter, and the profile knows nothing about dashboards.
            this.ShowSelectedDashboard();
        }

        private void UpdateMonitorList()
        {
            this.BuildMonitorPanes();
            this.UpdateMonitorStatus();
        }

        private void UpdateMonitorStatus()
        {
            if (this.monitorStatus == null || this.dashboardPackage == null)
            {
                return;
            }

            int traces = this.dashboardPackage.Monitors.Sum(m => m.Series.Count);
            int shown = this.dashboardPackage.Monitors
                .Where(m => m.Visible)
                .SelectMany(m => m.Series)
                .Count(s => s.Visible && s.IsBound);
            int unbound = this.dashboardPackage.Monitors.SelectMany(m => m.Series).Count(s => !s.IsBound);

            this.monitorStatus.Text =
                $"{this.dashboardPackage.Monitors.Count} monitors, {traces} traces, {shown} shown, {unbound} unbound."
                + "   Wheel zooms, click sets the cursor, arrows step it, right-click picks traces.";
        }

        /// <summary>
        /// Point the monitors at the session's history. Called when a new logger is built, because
        /// the columns - and therefore the history - change with the profile.
        /// </summary>
        private void SetMonitorHistory(LogHistory? value)
        {
            this.history = value;
            foreach (MonitorView view in this.monitorViews)
            {
                view.History = value;
                view.CursorSample = -1;
                view.Invalidate();
            }
        }

        /// <summary>
        /// Select the parameters the dashboard on screen displays. With <paramref name="replaceSelection"/>
        /// the grid is cleared first, so what is shown is what is logged. Returns how many parameters
        /// the dashboard selected.
        /// </summary>
        /// <remarks>
        /// This is the primary way of choosing what to log: put a gauge or a monitor trace on screen
        /// and its parameter is polled. The parameter grid remains for adding parameters that are
        /// logged but not displayed, and those are what the clear discards - they belonged to the
        /// dashboard that was showing. Opening a dashboard, and switching between dashboards, both
        /// replace; binding a single gauge does not, so one gauge cannot wipe the rest of a profile.
        ///
        /// Done by ticking rows in the parameter grid and letting the existing code rebuild the
        /// profile from it, rather than adding columns to the profile directly. The grid is the
        /// source of truth - CreateProfileFromGrid rebuilds the profile from the ticked rows, so a
        /// profile edited behind the grid's back would be undone by the next checkbox the user
        /// touched. Going through the grid also replaces the profile instance, which is what the
        /// logging thread watches for to rebuild its logger; mutating the existing one in place
        /// would change what is selected without the logger ever noticing.
        /// </remarks>
        private int SelectDashboardParameters(bool replaceSelection)
        {
            if (this.dashboardPackage == null || this.parameterGrid.Rows.Count == 0)
            {
                return 0;
            }

            HashSet<string> wanted = this.DashboardRequiredPids();

            // Nothing bound yet - an import where no gauge matched, say. Clearing on the strength of
            // that would throw away a selection and offer nothing in its place.
            if (wanted.Count == 0)
            {
                return 0;
            }

            int selected = 0;
            int cleared = 0;
            List<string> notAvailable = new List<string>();
            List<string> denied = new List<string>();

            try
            {
                this.suspendSelectionEvents = true;

                foreach (DataGridViewRow row in this.parameterGrid.Rows)
                {
                    if (!(row.Cells[CellIndexParameter].Value is Parameter parameter))
                    {
                        continue;
                    }

                    bool want = wanted.Remove(parameter.Id);

                    if (!want)
                    {
                        if (replaceSelection && (bool)row.Cells[CellIndexEnable].Value)
                        {
                            row.Cells[CellIndexEnable].Value = false;
                            row.Cells[CellIndexZoom].Value = false;
                            cleared++;
                        }

                        continue;
                    }

                    // Asking again for something the PCM has already refused would only have it
                    // refused again, once per row rebuild.
                    if (this.IsDeniedByPcm(parameter))
                    {
                        denied.Add(parameter.Name);
                        continue;
                    }

                    if (!(bool)row.Cells[CellIndexEnable].Value)
                    {
                        row.Cells[CellIndexEnable].Value = true;
                    }

                    // Match the conversion the package asked for; a gauge drawn in kPa must not be
                    // fed volts. Where it does not say, the parameter's first conversion stands.
                    PackagedPid? pid = this.dashboardPackage.FindPid(parameter.Id);
                    if (pid != null && !string.IsNullOrEmpty(pid.Units))
                    {
                        DataGridViewComboBoxCell unitsCell = (DataGridViewComboBoxCell)row.Cells[CellIndexUnits];
                        foreach (Conversion candidate in unitsCell.Items)
                        {
                            if (string.Equals(candidate.Units, pid.Units, StringComparison.OrdinalIgnoreCase))
                            {
                                unitsCell.Value = candidate;
                                break;
                            }
                        }
                    }

                    selected++;
                }
            }
            finally
            {
                this.suspendSelectionEvents = false;
            }

            // Anything left is bound to a parameter this PCM's operating system does not offer.
            notAvailable.AddRange(wanted);

            if (selected > 0 || cleared > 0)
            {
                this.LogProfileChanged();
            }

            if (notAvailable.Count > 0)
            {
                this.AddUserMessage(
                    "This dashboard uses parameters that are not available for this PCM: "
                    + string.Join(", ", notAvailable));
            }

            if (denied.Count > 0)
            {
                this.AddUserMessage(
                    "This PCM does not have these, so their gauges will stay blank: "
                    + string.Join(", ", denied));
            }

            this.ApplyDashboardLocks();
            return selected;
        }

        /// <summary>
        /// Re-assert the dashboard's requirements over a grid that has just been rebuilt or refilled
        /// from a profile. Adds rather than replaces, so a profile's own parameters survive.
        /// </summary>
        /// <remarks>
        /// Goes through the selection rather than only the locks so the dashboard's units are
        /// applied too: a profile may hold a different conversion for the same parameter, and the
        /// dashboard's gauge faces were drawn for the one the package names.
        /// </remarks>
        private void ReapplyDashboardSelection()
        {
            // The hook that matters for an auto-loaded dashboard: this runs when the grid is first
            // filled, which is the moment the parameter database exists and binding can finally
            // succeed. Until then the package has nothing to select.
            this.BindLoadedPackage();

            this.SelectDashboardParameters(replaceSelection: false);

            // Again because the selection returns early when nothing is bound, and stale locks from
            // a previous dashboard still need releasing.
            this.ApplyDashboardLocks();
        }

        /// <summary>
        /// Show the parameters a dashboard needs as ticked and locked in the parameter grid.
        /// </summary>
        /// <remarks>
        /// A gauge cannot draw a value that is not being polled, so a dashboard that displays a
        /// parameter forces it on. Letting it be unticked would leave a gauge reading nothing with
        /// no visible reason - the gauge would still look correctly bound. Locking the row says
        /// where the requirement comes from, and unbinding the gauge is what releases it.
        ///
        /// Re-applied whenever the grid or the dashboard changes, because rebuilding the grid
        /// recreates the rows and loses the styling with them.
        /// </remarks>
        private bool applyingDashboardLocks;

        private void ApplyDashboardLocks()
        {
            // LogProfileChanged below can lead back here; one pass is enough.
            if (this.parameterGrid.Rows.Count == 0 || this.applyingDashboardLocks)
            {
                return;
            }

            HashSet<string> required = this.DashboardRequiredPids();
            bool tickedSomething = false;

            try
            {
                this.applyingDashboardLocks = true;
                this.suspendSelectionEvents = true;

                foreach (DataGridViewRow row in this.parameterGrid.Rows)
                {
                    if (!(row.Cells[CellIndexParameter].Value is Parameter parameter))
                    {
                        continue;
                    }

                    // A PCM that has said it does not have this one outranks the dashboard. Left
                    // exactly as the supported-PID scan left it - neither ticked on the dashboard's
                    // behalf nor handed back to the user - and the gauge simply reads nothing.
                    if (this.IsDeniedByPcm(parameter))
                    {
                        continue;
                    }

                    DataGridViewCell enable = row.Cells[CellIndexEnable];
                    bool locked = required.Contains(parameter.Id);

                    if (locked)
                    {
                        if (!(bool)enable.Value)
                        {
                            enable.Value = true;
                            tickedSomething = true;
                        }

                        enable.ReadOnly = true;
                        enable.Style.ForeColor = AppTheme.MutedText;
                        enable.Style.BackColor = AppTheme.Raised;
                        enable.ToolTipText = "Required by the dashboard. Unbind the gauge to release it.";
                        row.Cells[CellIndexUnits].ReadOnly = true;
                        row.Cells[CellIndexUnits].ToolTipText = "Set by the dashboard.";
                    }
                    else if (enable.ReadOnly)
                    {
                        // Previously locked and no longer needed; hand it back to the user as it was.
                        // Cleared rather than set to a colour, so the cell goes back to inheriting
                        // the grid's own - which is what follows the theme.
                        enable.ReadOnly = false;
                        enable.Style.ForeColor = Color.Empty;
                        enable.Style.BackColor = Color.Empty;
                        enable.ToolTipText = string.Empty;
                        row.Cells[CellIndexUnits].ReadOnly = false;
                        row.Cells[CellIndexUnits].ToolTipText = string.Empty;
                    }
                }
            }
            finally
            {
                this.suspendSelectionEvents = false;
                this.applyingDashboardLocks = false;
            }

            // Ticking a row only changes the grid; the profile is rebuilt from it. Without this an
            // opened profile would show the dashboard's parameters ticked but never poll them.
            if (tickedSomething)
            {
                this.LogProfileChanged();
            }
        }

        /// <summary>
        /// The parameter ids currently on screen: the selected dashboard's gauges, and the traces of
        /// monitors that are shown.
        /// </summary>
        /// <remarks>
        /// Only the selected dashboard, not every dashboard in the package. A package may hold
        /// several, and polling the parameters of ones nobody is looking at would spend sample rate
        /// on values that are never drawn. Hidden monitors and unticked traces are skipped for the
        /// same reason.
        /// </remarks>
        private HashSet<string> DashboardRequiredPids()
        {
            HashSet<string> required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (this.dashboardPackage == null)
            {
                return required;
            }

            DashboardLayout? dashboard = this.SelectedDashboard;
            if (dashboard != null)
            {
                foreach (GaugeLayout gauge in dashboard.Gauges.Where(g => g.IsBound))
                {
                    required.Add(gauge.PidId!);
                }
            }

            foreach (MonitorSeriesLayout series in this.dashboardPackage.Monitors
                .Where(m => m.Visible)
                .SelectMany(m => m.Series)
                .Where(s => s.IsBound && s.Visible))
            {
                required.Add(series.PidId!);
            }

            return required;
        }

        /// <summary>Say how many gauges still need hooking up, which is the user's remaining work.</summary>
        private void UpdateDashboardStatus()
        {
            if (this.dashboardStatus == null || this.dashboardPackage == null)
            {
                return;
            }

            int gauges = this.dashboardPackage.Dashboards.Sum(d => d.Gauges.Count);
            int unbound = this.dashboardPackage.UnboundGaugeCount;
            string name = this.dashboardPath != null
                ? Path.GetFileName(this.dashboardPath)
                : this.dashboardPackage.Source.Name;

            this.dashboardStatus.Text = unbound == 0
                ? $"{name}: {gauges} gauges, all bound."
                : $"{name}: {gauges} gauges, {unbound} unbound - right-click a gauge to bind it.";
        }

        private DashboardLayout? SelectedDashboard => this.dashboardSelector?.SelectedItem as DashboardLayout;

        /// <summary>
        /// Draw the selected dashboard and make the logger poll what it shows.
        /// </summary>
        /// <remarks>
        /// Switching dashboards selects from scratch rather than adding: the parameters the previous
        /// one held are released, and this one's are selected. Without that, working through a
        /// package of several dashboards would accumulate every parameter any of them displayed and
        /// slow the whole log down.
        /// </remarks>
        private void ShowSelectedDashboard()
        {
            if (this.applyingPackage)
            {
                return;
            }

            DashboardLayout? layout = this.SelectedDashboard;

            if (this.dashboardView != null)
            {
                this.dashboardView.Dashboard = layout;
            }

            // Which dashboard is on screen belongs to the package, so saving and reopening comes
            // back to the same one.
            if (this.dashboardPackage != null && layout != null)
            {
                foreach (DashboardLayout dashboard in this.dashboardPackage.Dashboards)
                {
                    dashboard.Visible = ReferenceEquals(dashboard, layout);
                }
            }

            int selected = this.SelectDashboardParameters(replaceSelection: true);
            if (selected > 0)
            {
                this.AddUserMessage($"Selected {selected} parameters for this dashboard.");
            }
        }

        /// <summary>
        /// A gauge was bound or unbound by hand. Record the PID in the package so it survives a
        /// save, and select it so the gauge actually reads something.
        /// </summary>
        private void Gauge_BindingChanged(object? sender, GaugeLayout gauge)
        {
            if (this.dashboardPackage != null && gauge.IsBound && this.dashboardPackage.FindPid(gauge.PidId) == null)
            {
                Parameter? parameter = this.BindingCandidateParameter(gauge.PidId!);
                this.dashboardPackage.Pids.Add(new PackagedPid
                {
                    Id = gauge.PidId!,
                    Name = parameter?.Name ?? gauge.Title,
                    Units = parameter?.Conversions?.FirstOrDefault()?.Units ?? gauge.Units,
                });
            }

            this.UpdateDashboardStatus();

            // Add, don't replace: binding one gauge must not discard parameters the user picked by
            // hand in the grid.
            this.SelectDashboardParameters(replaceSelection: false);
        }

        private Parameter? BindingCandidateParameter(string id)
        {
            try
            {
                return this.database?.ListParametersBySupportedOs(this.osid)
                    .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception)
            {
                this.AddDebugMessage("Unable to look up " + id + ": " + exception.Message);
                return null;
            }
        }

        private void Gauge_EditRequested(object? sender, GaugeLayout gauge)
        {
            Parameter? parameter = gauge.IsBound ? this.BindingCandidateParameter(gauge.PidId!) : null;

            // The conversions this parameter actually offers, so the units cannot be set to something
            // the logger has no way to produce.
            List<string> units = parameter?.Conversions?.Select(c => c.Units).Distinct().ToList()
                ?? new List<string>();

            string previousUnits = gauge.Units;

            using (GaugePropertiesDialog dialog =
                new GaugePropertiesDialog(gauge, parameter?.Name ?? gauge.PidId ?? string.Empty, units))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            // The units choose which conversion is logged, so the package's PID entry has to agree -
            // otherwise the gauge is drawn in kPa while the logger is still polling volts.
            if (gauge.IsBound && !string.Equals(previousUnits, gauge.Units, StringComparison.OrdinalIgnoreCase))
            {
                PackagedPid? pid = this.dashboardPackage?.FindPid(gauge.PidId!);
                if (pid != null)
                {
                    pid.Units = gauge.Units;
                }

                this.SelectDashboardParameters(replaceSelection: false);
            }

            this.dashboardView?.Invalidate();
            this.UpdateDashboardStatus();
            this.SetDirtyFlag(true);
        }

        /// <summary>How far ahead of the display the logger is running, for diagnostics.</summary>
        private string DisplayFeedSummary =>
            $"{this.displayFeed.Produced} samples, {this.displayFeed.Delivered} drawn, {this.displayFeed.Dropped} dropped";
    }
}
