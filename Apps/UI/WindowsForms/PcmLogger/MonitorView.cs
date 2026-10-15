// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// A strip chart for one monitor: every visible trace over a moving time window, each scaled to
    /// its own range and drawn in its own colour.
    /// </summary>
    /// <remarks>
    /// Reads from <see cref="LogHistory"/> rather than being fed samples. That is what keeps it off
    /// the logging path: the logger appends at whatever rate the bus and the PCM allow, and this
    /// draws a window of whatever is there when its timer ticks. There is no queue between the two,
    /// so the chart cannot fall behind or flood - a redraw that is skipped costs nothing, because the
    /// samples are still in the history to be drawn next time.
    ///
    /// Reads go through ReadWindow, which takes the history's lock once per trace rather than once
    /// per point, and is the only accessor safe to use while the logging thread is appending.
    /// </remarks>
    internal class MonitorView : Control
    {
        /// <summary>Width an axis column takes for its tick labels, before its heading is measured.</summary>
        private const int AxisColumn = 46;

        /// <summary>
        /// Widest an axis column grows to for a long trace name. Past this the heading ellipsises
        /// rather than carrying on eating the plot.
        /// </summary>
        private const int MaximumAxisColumn = 86;

        /// <summary>Clear space either side of a heading, so neighbouring columns stay apart.</summary>
        private const int HeadingPadding = 8;

        /// <summary>Narrowest an axis column is allowed to get before the plot gives up width instead.</summary>
        private const int MinimumAxisColumn = 22;

        /// <summary>Point size of the axis headings, their readouts and the tick labels.</summary>
        private const float SmallEm = 6.5f;

        private const int MinimumPlotWidth = 100;

        private const int EdgePadding = 6;

        /// <summary>Where the axis headings begin: under the monitor's own name.</summary>
        private const int HeadingTop = 16;

        /// <summary>Clear space between the headings and the top of the plot.</summary>
        private const int HeadingGap = 3;

        private const int BottomGutter = 18;

        /// <summary>Zoom limits on the visible time window, in seconds.</summary>
        private const double MinimumSpanSeconds = 1;

        private const double MaximumSpanSeconds = 3600;

        /// <summary>Flags shared by every small label, so measuring and drawing agree.</summary>
        private const TextFormatFlags LabelFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        // The palette lives in AppTheme because a trace that reads well on white can vanish on a
        // dark background, so the colours have to change with the theme.

        private MonitorLayout? layout;

        /// <summary>Sample buffers, reused between paints so a redraw at 10 Hz allocates nothing.</summary>
        private double[] valueBuffer = new double[2048];

        private DateTime[] timeBuffer = new DateTime[2048];

        public MonitorView()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable,
                true);

            this.TabStop = true;
            this.BackColor = AppTheme.PlotBackground;
        }

        /// <summary>The monitor being drawn, including which traces are ticked.</summary>
        public MonitorLayout? MonitorLayout
        {
            get => this.layout;
            set
            {
                this.layout = value;
                this.SpanSeconds = value != null && value.TimeSpanSeconds > 0
                    ? value.TimeSpanSeconds
                    : 20;
                this.Invalidate();
            }
        }

        /// <summary>Where the samples come from. Null until logging has produced a row.</summary>
        public LogHistory? History { get; set; }

        /// <summary>How much time the plot spans. Changed by the mouse wheel.</summary>
        public double SpanSeconds { get; private set; } = 20;

        /// <summary>
        /// When true the view stops following the newest sample and stays where it is, so a trace can
        /// be read without it sliding away.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>
        /// Left edge of the window while paused, as a sample index. Ignored when running, where the
        /// window is always anchored to the newest sample.
        /// </summary>
        public int ScrollSample { get; set; }

        /// <summary>The sample the cursor sits on, or -1 for none. Placed by clicking, moved by arrows.</summary>
        public int CursorSample { get; set; } = -1;

        /// <summary>Raised when the view's scroll range or position changes, so a scrollbar can follow.</summary>
        public event EventHandler? ViewChanged;

        /// <summary>
        /// Raised when the user moves the cursor here, so every other pane can show the same moment.
        /// </summary>
        public event EventHandler? CursorMoved;

        /// <summary>
        /// Raised when the user zooms here, so every other pane can cover the same span of time.
        /// </summary>
        public event EventHandler? SpanChanged;

        /// <summary>
        /// How close to the window edge the cursor may get before the window follows it, as a
        /// fraction of the window. Scrolling only once the cursor has already left put the sample
        /// being read at the very edge, with nothing after it to see.
        /// </summary>
        private const double CursorEdgeMargin = 0.15;

        /// <summary>The cursor's time from the start of the log, or null when there is no cursor.</summary>
        public TimeSpan? CursorElapsed
        {
            get
            {
                LogHistory? history = this.History;
                if (history == null || history.Count == 0 || this.CursorSample < 0)
                {
                    return null;
                }

                DateTime at = history.TimestampOrDefault(Math.Min(this.CursorSample, history.Count - 1));
                DateTime origin = history.TimestampOrDefault(0);
                return at < origin ? TimeSpan.Zero : at - origin;
            }
        }

        /// <summary>
        /// Put the cursor on a sample and bring it into view, without telling anyone: this is how a
        /// pane is brought into line with another, and echoing it back would loop.
        /// </summary>
        public void ShowCursorAt(int sample)
        {
            LogHistory? history = this.History;
            if (history == null || history.Count == 0)
            {
                this.CursorSample = -1;
                this.Invalidate();
                return;
            }

            this.PlaceCursor(history, sample);
            this.Invalidate();
        }

        /// <summary>
        /// Put the cursor on a sample, and decide what the window does about it.
        /// </summary>
        /// <remarks>
        /// Landing on the newest sample resumes following the live end. Without that there is no way
        /// back to live once the cursor has been moved: stepping to the end is the obvious gesture
        /// for "catch up", so it is the one that does it.
        /// </remarks>
        private void PlaceCursor(LogHistory history, int sample)
        {
            this.CursorSample = Math.Max(0, Math.Min(history.Count - 1, sample));

            if (this.CursorSample == history.Count - 1)
            {
                this.Paused = false;
                this.ViewChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            this.BringCursorIntoView(history);
        }

        /// <summary>
        /// The sample at a given time from the start of the log, for jumping to a typed time.
        /// </summary>
        public bool TryGetSampleAt(TimeSpan elapsed, out int sample)
        {
            sample = -1;

            LogHistory? history = this.History;
            if (history == null || history.Count == 0)
            {
                return false;
            }

            DateTime origin = history.TimestampOrDefault(0);
            int found = history.IndexAtOrBefore(origin + elapsed);
            sample = Math.Max(0, Math.Min(history.Count - 1, found));
            return true;
        }

        /// <summary>
        /// Keep the cursor away from the window's edges, dragging the window along with it.
        /// </summary>
        private void BringCursorIntoView(LogHistory history)
        {
            int window = this.WindowSamples(history);
            if (history.Count <= window)
            {
                // The whole log is on screen; there is nothing to scroll.
                return;
            }

            int start = this.WindowStart(history, window);
            int margin = Math.Max(1, (int)(window * CursorEdgeMargin));

            if (this.CursorSample >= start + margin && this.CursorSample < start + window - margin)
            {
                return;
            }

            this.Paused = true;
            this.ScrollSample = Math.Max(
                0, Math.Min(this.CursorSample - (window / 2), history.Count - window));

            this.ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised when a trace is ticked or unticked, so the PID selection can be redone.</summary>
        public event EventHandler? SeriesVisibilityChanged;

        private IEnumerable<MonitorSeriesLayout> VisibleSeries =>
            this.layout == null
                ? Enumerable.Empty<MonitorSeriesLayout>()
                : this.layout.Series.Where(s => s.Visible && s.IsBound);

        /// <summary>
        /// How the axis columns are shared between the two ends. The left takes the extra one when
        /// the count is odd, so three traces go two left, one right rather than piling up on a side.
        /// </summary>
        private void AxisLayout(out int leftCount, out int rightCount, out int columnWidth)
        {
            int count = this.VisibleSeries.Count();
            leftCount = (count + 1) / 2;
            rightCount = count - leftCount;

            if (count == 0)
            {
                columnWidth = 0;
                return;
            }

            // Axes never squeeze the plot below a usable width; they narrow first, and the labels
            // ellipsise, rather than leaving nowhere to draw the traces.
            int available = Math.Max(0, this.ClientSize.Width - MinimumPlotWidth - (EdgePadding * 2));
            columnWidth = Math.Max(MinimumAxisColumn, Math.Min(this.PreferredAxisColumn(), available / count));
        }

        /// <summary>
        /// How wide an axis column wants to be: enough for the longest heading it has to carry.
        /// </summary>
        /// <remarks>
        /// Measured rather than assumed. A fixed width was sized for tick labels, which are three or
        /// four digits, and then had to carry a trace name as well - so anything past about eight
        /// characters arrived as "Engine S". The plot is still protected, by the clamp above and by
        /// the ceiling here.
        /// </remarks>
        private int PreferredAxisColumn()
        {
            int preferred = AxisColumn;

            foreach (MonitorSeriesLayout trace in this.VisibleSeries)
            {
                int width = TextRenderer.MeasureText(
                    trace.Title, this.SmallFont, new Size(int.MaxValue, int.MaxValue), LabelFlags).Width;

                preferred = Math.Max(preferred, Math.Min(MaximumAxisColumn, width + HeadingPadding));
            }

            return preferred;
        }

        /// <summary>
        /// The font every small label in this view is measured and drawn with.
        /// </summary>
        /// <remarks>
        /// Held rather than made per draw, because the layout now measures with it as well: a font
        /// built inside a property that the paint path calls several times would be churn, and
        /// measuring with a different font than the one drawn is how text stops fitting.
        /// </remarks>
        private Font SmallFont => this.smallFont ??= new Font(this.Font.FontFamily, SmallEm);

        private Font? smallFont;

        /// <summary>
        /// How tall one line of the small font actually is.
        /// </summary>
        /// <remarks>
        /// Measured rather than assumed. The headings were laid out in fixed nine-pixel rows, which
        /// is shorter than the font draws at any scaling, so the name and the reading under it were
        /// both clipped along the bottom and overlapped each other by a pixel.
        /// </remarks>
        private int SmallLineHeight => TextRenderer.MeasureText(
            "Ag", this.SmallFont, new Size(int.MaxValue, int.MaxValue), LabelFlags).Height;

        /// <summary>
        /// Room above the plot for the monitor's name, then a heading and a reading per axis.
        /// </summary>
        private int TopGutter => HeadingTop + (this.SmallLineHeight * 2) + HeadingGap;

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            this.smallFont?.Dispose();
            this.smallFont = null;
            this.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.smallFont?.Dispose();
                this.smallFont = null;
            }

            base.Dispose(disposing);
        }

        private Rectangle PlotArea
        {
            get
            {
                this.AxisLayout(out int leftCount, out int rightCount, out int columnWidth);

                int left = EdgePadding + (leftCount * columnWidth);
                int right = EdgePadding + (rightCount * columnWidth);

                int width = Math.Max(1, this.ClientSize.Width - left - right);
                int top = this.TopGutter;
                int height = Math.Max(1, this.ClientSize.Height - top - BottomGutter);
                return new Rectangle(left, top, width, height);
            }
        }

        /// <summary>
        /// The column one axis occupies. Index 0 is the innermost on its side, so axes stack outward
        /// from the plot as traces are added.
        /// </summary>
        private Rectangle AxisBounds(Rectangle plot, int slot, bool onLeft, int columnWidth)
        {
            if (onLeft)
            {
                return new Rectangle(
                    plot.X - ((slot + 1) * columnWidth), plot.Y, columnWidth, plot.Height);
            }

            return new Rectangle(plot.Right + (slot * columnWidth), plot.Y, columnWidth, plot.Height);
        }

        /// <summary>
        /// How many samples the window holds, estimated from the session's average rate. Sampling is
        /// not perfectly regular, so this is a window size rather than an exact mapping; positions
        /// come from each sample's own timestamp.
        /// </summary>
        private int WindowSamples(LogHistory history)
        {
            if (history.Count < 2)
            {
                return history.Count;
            }

            double seconds = history.Duration.TotalSeconds;
            double rate = seconds > 0 ? history.Count / seconds : 20;
            int samples = (int)Math.Ceiling(this.SpanSeconds * Math.Max(1, rate));
            return Math.Max(2, Math.Min(samples, history.Count));
        }

        private int WindowStart(LogHistory history, int windowSamples)
        {
            if (!this.Paused)
            {
                return Math.Max(0, history.Count - windowSamples);
            }

            return Math.Max(0, Math.Min(this.ScrollSample, Math.Max(0, history.Count - windowSamples)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            // Zoom about the right edge, which is where the live data is.
            double factor = e.Delta > 0 ? 0.8 : 1.25;
            if (!this.SetSpanSeconds(this.SpanSeconds * factor))
            {
                return;
            }

            this.ViewChanged?.Invoke(this, EventArgs.Empty);
            this.SpanChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set the visible time window, clamped to the zoom limits. False when it did not move.
        /// </summary>
        /// <remarks>
        /// Public so the panes can be held together. They are views of one session, and reading one
        /// against another only works when they cover the same span of time - so zooming one zooms
        /// the rest, in the same way that moving the cursor in one moves it in all of them.
        /// </remarks>
        public bool SetSpanSeconds(double seconds)
        {
            double updated = Math.Max(MinimumSpanSeconds, Math.Min(MaximumSpanSeconds, seconds));
            if (Math.Abs(updated - this.SpanSeconds) < 0.0001)
            {
                return false;
            }

            this.SpanSeconds = updated;

            // Kept on the layout as well, so the span is part of the saved package rather than
            // something the user has to set again every time they open it.
            if (this.layout != null)
            {
                this.layout.TimeSpanSeconds = updated;
            }

            this.Invalidate();
            return true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Otherwise the arrow keys move focus between controls instead of reaching OnKeyDown.
            if (keyData == Keys.Left || keyData == Keys.Right
                || keyData == Keys.Home || keyData == Keys.End)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            LogHistory? history = this.History;
            if (history == null || history.Count == 0)
            {
                return;
            }

            // With no cursor yet, the keys start from the newest sample, which is what is on screen.
            int moved = this.CursorSample >= 0 ? this.CursorSample : history.Count - 1;
            switch (e.KeyCode)
            {
                case Keys.Left:
                    moved--;
                    break;

                case Keys.Right:
                    moved++;
                    break;

                case Keys.Home:
                    moved = 0;
                    break;

                case Keys.End:
                    moved = history.Count - 1;
                    break;

                default:
                    return;
            }

            this.PlaceCursor(history, moved);

            e.Handled = true;
            this.CursorMoved?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.Focus();

            if (e.Button == MouseButtons.Right)
            {
                this.ShowSeriesMenu(e.Location);
                return;
            }

            LogHistory? history = this.History;
            Rectangle plot = this.PlotArea;
            if (history == null || history.Count == 0 || !plot.Contains(e.Location))
            {
                return;
            }

            int window = this.WindowSamples(history);
            int start = this.WindowStart(history, window);
            double fraction = (e.X - plot.X) / (double)Math.Max(1, plot.Width);

            this.CursorSample = Math.Max(0, Math.Min(history.Count - 1, start + (int)(fraction * window)));

            this.CursorMoved?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }

        /// <summary>
        /// Everything that could be plotted here, id to display name. Set by the host from what the
        /// logger is actually producing.
        /// </summary>
        public IReadOnlyDictionary<string, string>? AvailableParameters { get; set; }

        /// <summary>
        /// The trace picker: every parameter available, ticked where it is on the plot.
        /// </summary>
        /// <remarks>
        /// Lists what could be shown, not just what is. Offering only the monitor's existing traces
        /// made this a way to remove them and nothing else - adding one meant editing the package by
        /// hand. A parameter ticked on here that the monitor has never carried gets a series created
        /// for it, which is what makes the menu a way to build a monitor rather than only prune one.
        /// </remarks>
        private void ShowSeriesMenu(Point location)
        {
            if (this.layout == null)
            {
                return;
            }

            ContextMenuStrip menu = new ContextMenuStrip();

            // The monitor's own traces first, in its order, so a package's intent stays visible.
            HashSet<string> listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MonitorSeriesLayout series in this.layout.Series)
            {
                menu.Items.Add(this.BuildSeriesItem(series));
                if (series.IsBound)
                {
                    listed.Add(series.PidId!);
                }
            }

            // Then everything else being logged, which the monitor could show but does not yet.
            List<KeyValuePair<string, string>> others = (this.AvailableParameters
                    ?? new Dictionary<string, string>())
                .Where(p => !listed.Contains(p.Key))
                .OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (others.Count > 0)
            {
                if (menu.Items.Count > 0)
                {
                    menu.Items.Add(new ToolStripSeparator());
                }

                foreach (KeyValuePair<string, string> parameter in others)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(parameter.Value) { Checked = false };

                    string id = parameter.Key;
                    string name = parameter.Value;
                    item.Click += (s, e) =>
                    {
                        this.AddSeries(id, name);
                        this.SeriesVisibilityChanged?.Invoke(this, EventArgs.Empty);
                        this.Invalidate();
                    };

                    menu.Items.Add(item);
                }
            }

            if (menu.Items.Count == 0)
            {
                menu.Items.Add(
                    new ToolStripMenuItem("Nothing to plot - start logging first") { Enabled = false });
            }

            menu.Show(this, location);
        }

        private ToolStripMenuItem BuildSeriesItem(MonitorSeriesLayout series)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(
                series.IsBound ? series.Title : series.Title + "   [unbound]")
            {
                Checked = series.Visible && series.IsBound,
                Enabled = series.IsBound,
            };

            MonitorSeriesLayout captured = series;
            item.Click += (s, e) =>
            {
                captured.Visible = !captured.Visible;
                this.SeriesVisibilityChanged?.Invoke(this, EventArgs.Empty);
                this.Invalidate();
            };

            return item;
        }

        /// <summary>
        /// Add a trace for a parameter the monitor did not carry. No range is known for it, so it
        /// auto-scales to what is on screen until the package is given one.
        /// </summary>
        private void AddSeries(string pidId, string name)
        {
            if (this.layout == null)
            {
                return;
            }

            Color color = AppTheme.SeriesColor(this.layout.Series.Count);

            this.layout.Series.Add(new MonitorSeriesLayout
            {
                PidId = pidId,
                Title = name,
                Visible = true,
                Color = (color.R << 16) | (color.G << 8) | color.B,
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(this.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle plot = this.PlotArea;
            using (Pen frame = new Pen(AppTheme.Line))
            {
                graphics.DrawRectangle(frame, plot);
            }

            LogHistory? history = this.History;
            List<MonitorSeriesLayout> series = this.VisibleSeries.ToList();

            if (this.layout == null)
            {
                this.DrawCentredMessage(graphics, plot, "No monitors in this dashboard.");
                return;
            }

            if (history == null || history.Count < 2)
            {
                this.DrawTitle(graphics);
                this.DrawCentredMessage(graphics, plot, "Waiting for data - start logging to see traces.");
                return;
            }

            if (series.Count == 0)
            {
                this.DrawTitle(graphics);
                this.DrawCentredMessage(graphics, plot, "No traces shown - right-click to choose some.");
                return;
            }

            int window = this.WindowSamples(history);
            int start = this.WindowStart(history, window);

            this.EnsureBuffers(window);

            int cursorX = -1;
            if (this.CursorSample >= start && this.CursorSample < start + window)
            {
                cursorX = plot.X + (int)((this.CursorSample - start) / (double)Math.Max(1, window) * plot.Width);
            }

            this.AxisLayout(out int leftCount, out _, out int columnWidth);

            // The sample each axis reads out: where the cursor is, or the newest when there is none.
            int readoutSample = this.CursorSample >= 0 && this.CursorSample < history.Count
                ? this.CursorSample
                : history.Count - 1;

            for (int index = 0; index < series.Count; index++)
            {
                MonitorSeriesLayout trace = series[index];
                Color color = ColorOf(trace, index);
                int column = history.IndexOfColumn(trace.PidId!);
                if (column < 0)
                {
                    continue;
                }

                int read = history.ReadWindow(column, start, window, this.valueBuffer, this.timeBuffer);

                // One range drives both the trace and its axis, so what is drawn and what is
                // labelled cannot disagree. Two traces sharing a range therefore overlay directly -
                // which is the point of a wideband against its target.
                this.EffectiveRange(trace, read, out double low, out double high);

                if (read > 1)
                {
                    DrawTrace(graphics, plot, color, this.valueBuffer, read, low, high);
                }

                bool onLeft = index < leftCount;
                int slot = onLeft ? index : index - leftCount;
                Rectangle axis = this.AxisBounds(plot, slot, onLeft, columnWidth);

                this.DrawAxis(graphics, axis, plot, trace, color, low, high, onLeft);
                this.DrawAxisHeading(
                    graphics, axis, trace, color, history.ValueOrNaN(readoutSample, column));

                // The reading where the cursor crosses this trace, on the trace, in its colour.
                if (cursorX >= 0)
                {
                    this.DrawCursorValue(
                        graphics, plot, color, cursorX,
                        history.ValueOrNaN(this.CursorSample, column), low, high);
                }
            }

            this.DrawTimeAxis(graphics, plot, history, start, window);

            if (cursorX >= 0)
            {
                using (Pen cursor = new Pen(AppTheme.Text) { DashStyle = DashStyle.Dot })
                {
                    graphics.DrawLine(cursor, cursorX, plot.Top, cursorX, plot.Bottom);
                }
            }

            this.DrawTitle(graphics);
        }

        /// <summary>
        /// The range a trace is drawn and labelled against: what the package defines for it, or what
        /// is on screen when the package defines nothing usable.
        /// </summary>
        /// <remarks>
        /// The defined range is preferred precisely so two traces can be compared. Auto-scaling every
        /// trace to its own window would make any pair look like they track each other, which is the
        /// opposite of what the chart is for.
        /// </remarks>
        private void EffectiveRange(MonitorSeriesLayout trace, int count, out double low, out double high)
        {
            if (trace.RangeHigh > trace.RangeLow)
            {
                low = trace.RangeLow;
                high = trace.RangeHigh;
                return;
            }

            low = double.MaxValue;
            high = double.MinValue;
            for (int index = 0; index < count; index++)
            {
                double sample = this.valueBuffer[index];
                if (double.IsNaN(sample))
                {
                    continue;
                }

                low = Math.Min(low, sample);
                high = Math.Max(high, sample);
            }

            if (low > high)
            {
                low = 0;
                high = 1;
            }
            else if (Math.Abs(high - low) < 0.0001)
            {
                low -= 1;
                high += 1;
            }
        }

        private void EnsureBuffers(int window)
        {
            if (this.valueBuffer.Length < window)
            {
                this.valueBuffer = new double[window];
                this.timeBuffer = new DateTime[window];
            }
        }

        private static void DrawTrace(
            Graphics graphics,
            Rectangle plot,
            Color color,
            double[] buffer,
            int count,
            double low,
            double high)
        {
            double span = high - low;
            if (span <= 0)
            {
                return;
            }

            List<PointF> points = new List<PointF>(count);

            for (int index = 0; index < count; index++)
            {
                double sample = buffer[index];
                if (double.IsNaN(sample))
                {
                    // A gap in this column: break the line rather than drawing through it.
                    if (points.Count > 1)
                    {
                        using (Pen pen = new Pen(color, 1.5f))
                        {
                            graphics.DrawLines(pen, points.ToArray());
                        }
                    }

                    points.Clear();
                    continue;
                }

                double fraction = (sample - low) / span;
                fraction = Math.Max(0, Math.Min(1, fraction));

                points.Add(new PointF(
                    plot.X + ((float)index / Math.Max(1, count - 1) * plot.Width),
                    plot.Bottom - (float)(fraction * plot.Height)));
            }

            if (points.Count > 1)
            {
                using (Pen pen = new Pen(color, 1.5f))
                {
                    graphics.DrawLines(pen, points.ToArray());
                }
            }
        }

        /// <summary>
        /// One Y axis in the trace's colour: a rule down the edge of its column, tick marks, and the
        /// scale labelled from the trace's own range.
        /// </summary>
        /// <remarks>
        /// The colour is the whole point. Several traces share one plot, and without an axis in the
        /// same colour there is no way to tell what any of them is measured against - a line halfway
        /// up could be 4000 RPM or 14.7 AFR. Axes stack outward from the plot, left and right, the
        /// way TunerPro arranges them.
        /// </remarks>
        private void DrawAxis(
            Graphics graphics,
            Rectangle axis,
            Rectangle plot,
            MonitorSeriesLayout trace,
            Color color,
            double low,
            double high,
            bool onLeft)
        {
            // The rule sits against the plot, so the innermost axis touches it and the rest line up
            // outside in order.
            int ruleX = onLeft ? axis.Right - 1 : axis.Left;

            using (Pen pen = new Pen(color))
            {
                graphics.DrawLine(pen, ruleX, plot.Top, ruleX, plot.Bottom);

                int ticks = Math.Max(2, Math.Min(6, plot.Height / 34));

                for (int index = 0; index < ticks; index++)
                {
                    double fraction = index / (double)(ticks - 1);
                    int y = plot.Bottom - (int)(fraction * plot.Height);
                    int mark = onLeft ? -4 : 4;

                    graphics.DrawLine(pen, ruleX, y, ruleX + mark, y);

                    // The topmost label would sit half outside the plot, so it is nudged down and
                    // the bottom one up; every other tick is centred on its mark.
                    int labelY = y - 5;
                    labelY = Math.Max(plot.Top, Math.Min(plot.Bottom - 10, labelY));

                    Rectangle labelArea = onLeft
                        ? new Rectangle(axis.Left, labelY, axis.Width - 6, 10)
                        : new Rectangle(axis.Left + 6, labelY, axis.Width - 6, 10);

                    TextRenderer.DrawText(
                        graphics,
                        FormatTick(low + ((high - low) * fraction)),
                        this.SmallFont,
                        labelArea,
                        color,
                        (onLeft ? TextFormatFlags.Right : TextFormatFlags.Left)
                        | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                        | TextFormatFlags.EndEllipsis);
                }
            }
        }

        /// <summary>
        /// The reading at the cursor, drawn where the cursor line crosses that trace and in the
        /// trace's colour, as TunerPro does it.
        /// </summary>
        /// <remarks>
        /// On the trace rather than in a legend because several traces share the plot: a column of
        /// numbers somewhere else leaves the reader matching colours by eye, while a number sitting
        /// on the line it came from needs no matching at all.
        /// </remarks>
        private void DrawCursorValue(
            Graphics graphics,
            Rectangle plot,
            Color color,
            int cursorX,
            double value,
            double low,
            double high)
        {
            if (double.IsNaN(value) || high <= low)
            {
                return;
            }

            double fraction = Math.Max(0, Math.Min(1, (value - low) / (high - low)));
            int y = plot.Bottom - (int)(fraction * plot.Height);

            using (Font small = new Font(this.Font.FontFamily, 7f, FontStyle.Bold))
            {
                string text = value.ToString("0.##");
                Size measured = TextRenderer.MeasureText(text, small, Size.Empty, LabelFlags);

                // Just above the point and to the right of the cursor, flipping to the left when
                // that would run past the edge of the plot.
                int x = cursorX + 4;
                if (x + measured.Width > plot.Right)
                {
                    x = cursorX - 4 - measured.Width;
                }

                int top = Math.Max(plot.Top, Math.Min(plot.Bottom - measured.Height, y - measured.Height - 2));

                // A halo, so a reading stays legible where it crosses another trace or a gridline.
                using (Brush halo = new SolidBrush(Color.FromArgb(200, this.BackColor)))
                {
                    graphics.FillRectangle(halo, x - 1, top, measured.Width + 2, measured.Height);
                }

                TextRenderer.DrawText(
                    graphics, text, small,
                    new Rectangle(x, top, measured.Width + 2, measured.Height), color, LabelFlags);
            }
        }

        /// <summary>
        /// The heading over an axis column: the trace's name and its value at the cursor, in the
        /// trace's colour, so a reading is next to the scale it belongs to.
        /// </summary>
        private void DrawAxisHeading(
            Graphics graphics, Rectangle axis, MonitorSeriesLayout trace, Color color, double value)
        {
            int line = this.SmallLineHeight;

            TextRenderer.DrawText(
                graphics, trace.Title, this.SmallFont,
                new Rectangle(axis.Left, HeadingTop, axis.Width, line), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            string text = double.IsNaN(value) ? "--" : value.ToString("0.##");
            TextRenderer.DrawText(
                graphics, text, this.SmallFont,
                new Rectangle(axis.Left, HeadingTop + line, axis.Width, line), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawTimeAxis(
            Graphics graphics, Rectangle plot, LogHistory history, int start, int window)
        {
            DateTime first = history.TimestampOrDefault(start);
            DateTime last = history.TimestampOrDefault(Math.Min(history.Count - 1, start + window - 1));
            DateTime origin = history.TimestampOrDefault(0);

            if (first == DateTime.MinValue || last == DateTime.MinValue)
            {
                return;
            }

            using (Font small = new Font(this.Font.FontFamily, 7f))
            {
                // Elapsed time from the start of the session, in M:S, as TunerPro shows it.
                TextRenderer.DrawText(
                    graphics, FormatElapsed(first - origin), small,
                    new Rectangle(plot.X, plot.Bottom + 2, 60, BottomGutter - 2), AppTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);

                TextRenderer.DrawText(
                    graphics, FormatElapsed(last - origin), small,
                    new Rectangle(plot.Right - 60, plot.Bottom + 2, 60, BottomGutter - 2), AppTheme.Text,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);

                string span = this.SpanSeconds >= 60
                    ? (this.SpanSeconds / 60).ToString("0.#") + " min span"
                    : this.SpanSeconds.ToString("0.#") + " s span";

                TextRenderer.DrawText(
                    graphics, span, small,
                    new Rectangle(plot.X, plot.Bottom + 2, plot.Width, BottomGutter - 2), AppTheme.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }

        private void DrawTitle(Graphics graphics)
        {
            string title = this.layout?.Title ?? string.Empty;
            if (this.Paused)
            {
                title += "   (stopped)";
            }

            // Top row only: the row below belongs to the axis headings.
            TextRenderer.DrawText(
                graphics, title, this.Font, new Rectangle(2, 2, this.ClientSize.Width - 4, 13),
                AppTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis);
        }

        private void DrawCentredMessage(Graphics graphics, Rectangle plot, string message)
        {
            TextRenderer.DrawText(
                graphics, message, this.Font, plot, AppTheme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>
        /// The trace's own colour, falling back to a palette when the package gives it none - an ADX
        /// import can leave a series black, and several black traces cannot be told apart.
        /// </summary>
        private static Color ColorOf(MonitorSeriesLayout trace, int slot)
        {
            Color color = Color.FromArgb(
                (trace.Color >> 16) & 0xFF, (trace.Color >> 8) & 0xFF, trace.Color & 0xFF);

            if (color.R == 0 && color.G == 0 && color.B == 0)
            {
                return AppTheme.SeriesColor(slot);
            }

            return color;
        }

        private static string FormatTick(double value) =>
            Math.Abs(value) >= 1000 ? value.ToString("0") : value.ToString("0.#");

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return ((int)elapsed.TotalMinutes).ToString("0") + ":" + elapsed.Seconds.ToString("00");
        }
    }
}
