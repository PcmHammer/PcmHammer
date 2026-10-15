// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Draws a dashboard: round gauges and text readouts, positioned by the layout's fractional
    /// bounds so the arrangement holds at any size.
    /// </summary>
    /// <remarks>
    /// Owner-drawn rather than a control per gauge. A dashboard is a few dozen gauges refreshed
    /// many times a second; that many child controls each invalidating separately flickers and
    /// costs far more than one buffered paint.
    /// </remarks>
    public class DashboardView : Control
    {
        private DashboardLayout? layout;
        private readonly Dictionary<string, double> values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Parameter id to display name, from the most recent row.
        /// </summary>
        /// <remarks>
        /// The bind menu is built from what is actually arriving rather than from the parameter
        /// database, so it can only ever offer parameters that will really produce a value. Binding
        /// a gauge to something the current profile does not log would leave it reading nothing with
        /// no indication why.
        /// </remarks>
        private readonly SortedDictionary<string, string> available = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public DashboardView()
        {
            // The gauges repaint continuously, so draw the whole surface off-screen.
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw,
                true);

            this.BackColor = AppTheme.PlotBackground;
        }

        /// <summary>Raised when the user asks to edit a gauge.</summary>
        public event EventHandler<GaugeLayout>? GaugeEditRequested;

        /// <summary>Raised when a gauge is bound to a parameter, or unbound.</summary>
        public event EventHandler<GaugeLayout>? GaugeBindingChanged;

        /// <summary>The dashboard being drawn. Named to avoid hiding Control.Layout, which is an event.</summary>
        public DashboardLayout? Dashboard
        {
            get => this.layout;
            set
            {
                this.layout = value;
                this.Invalidate();
            }
        }

        /// <summary>
        /// Supply the latest readings, keyed by parameter id. Gauges with no binding, or whose
        /// parameter is absent from the row, show no value.
        /// </summary>
        public void SetValues(IEnumerable<LogRowElement> row)
        {
            this.values.Clear();
            if (row != null)
            {
                foreach (LogRowElement element in row)
                {
                    this.values[element.ParameterId] = element.ValueAsNumber;
                    this.available[element.ParameterId] = element.ParameterName;
                }
            }

            this.Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Right || this.layout == null)
            {
                return;
            }

            GaugeLayout? gauge = this.HitTest(e.Location);
            if (gauge == null)
            {
                return;
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Edit " + gauge.Title + "...", null, (s, a) => this.GaugeEditRequested?.Invoke(this, gauge));
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem bind = new ToolStripMenuItem("Bind to");
            if (this.available.Count == 0)
            {
                // Nothing has been logged yet, so there is nothing honest to offer.
                bind.DropDownItems.Add(new ToolStripMenuItem("(start logging to see parameters)") { Enabled = false });
            }
            else
            {
                foreach (KeyValuePair<string, string> candidate in this.available)
                {
                    string id = candidate.Key;
                    ToolStripMenuItem item = new ToolStripMenuItem(candidate.Value)
                    {
                        Checked = string.Equals(gauge.PidId, id, StringComparison.OrdinalIgnoreCase),
                    };

                    item.Click += (s, a) =>
                    {
                        gauge.PidId = id;
                        this.Invalidate();
                        this.GaugeBindingChanged?.Invoke(this, gauge);
                    };

                    bind.DropDownItems.Add(item);
                }
            }

            menu.Items.Add(bind);

            if (gauge.IsBound)
            {
                menu.Items.Add("Unbind", null, (s, a) =>
                {
                    gauge.PidId = null;
                    this.Invalidate();
                    this.GaugeBindingChanged?.Invoke(this, gauge);
                });
            }

            menu.Show(this, e.Location);
        }

        private GaugeLayout? HitTest(Point point)
        {
            if (this.layout == null)
            {
                return null;
            }

            foreach (GaugeLayout gauge in this.layout.Gauges)
            {
                if (this.BoundsOf(gauge).Contains(point))
                {
                    return gauge;
                }
            }

            return null;
        }

        private Rectangle BoundsOf(GaugeLayout gauge)
        {
            return new Rectangle(
                (int)Math.Round(gauge.Left * this.ClientSize.Width),
                (int)Math.Round(gauge.Top * this.ClientSize.Height),
                Math.Max(1, (int)Math.Round(gauge.Width * this.ClientSize.Width)),
                Math.Max(1, (int)Math.Round(gauge.Height * this.ClientSize.Height)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (this.layout == null || this.layout.Gauges.Count == 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    "No dashboard loaded." + Environment.NewLine + "Use Import ADX to load one.",
                    this.Font,
                    this.ClientRectangle,
                    AppTheme.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            foreach (GaugeLayout gauge in this.layout.Gauges)
            {
                Rectangle bounds = this.BoundsOf(gauge);
                if (bounds.Width < 4 || bounds.Height < 4)
                {
                    continue;
                }

                bounds.Inflate(-2, -2);
                if (gauge.Kind == GaugeKind.Round)
                {
                    this.DrawRoundGauge(graphics, bounds, gauge);
                }
                else
                {
                    this.DrawTextGauge(graphics, bounds, gauge);
                }
            }
        }

        /// <summary>The current reading, or null when the gauge is unbound or has no data yet.</summary>
        private double? ValueOf(GaugeLayout gauge)
        {
            if (!gauge.IsBound || !this.values.TryGetValue(gauge.PidId!, out double value))
            {
                return null;
            }

            return double.IsNaN(value) ? (double?)null : value;
        }

        /// <summary>
        /// The colour a reading is drawn in: the gauge's own normal and alarm colours, which both the
        /// package and the gauge editor set.
        /// </summary>
        private Color ColorFor(GaugeLayout gauge, double? value)
        {
            if (value == null)
            {
                return AppTheme.MutedText;
            }

            bool alarming = gauge.HasAlarms
                && (value.Value < gauge.AlarmLow || value.Value > gauge.AlarmHigh);

            return FromPacked(alarming ? gauge.AlarmColor : gauge.NormalColor);
        }

        /// <summary>A 0xRRGGBB value as a Color, falling back when a package leaves it black.</summary>
        private static Color FromPacked(int packed)
        {
            Color color = Color.FromArgb((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);

            // Black is what an unset colour looks like, and a black reading on a white face reads as
            // a drawing fault rather than a choice.
            return color.ToArgb() == Color.Black.ToArgb() ? AppTheme.UnsetGauge : color;
        }

        /// <summary>Smallest font this view will shrink to before it gives up and clips.</summary>
        private const float MinimumFontSize = 6f;

        /// <summary>
        /// Flags shared by every measurement and every draw in this view.
        /// </summary>
        /// <remarks>
        /// Measuring and drawing MUST use the same flags or the fit is a fiction. TextRenderer adds
        /// padding by default, so a measurement taken without these reports a width the draw then
        /// exceeds - which is why sizing text to a measured width still produced "0...." in a box
        /// with room to spare. NoPadding makes the two agree; SingleLine keeps a value on one line so
        /// the height measured is the height drawn.
        /// </remarks>
        private const TextFormatFlags FitFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        /// <summary>
        /// The largest font up to <paramref name="startEm"/> that draws <paramref name="text"/> inside
        /// <paramref name="area"/>. The caller owns the returned font.
        /// </summary>
        /// <remarks>
        /// Width has to be measured, not assumed. Sizing on height alone is what made a five-figure
        /// reading render as "22..." in a box with room for it: the font was chosen before anyone knew
        /// how wide the digits were.
        ///
        /// Iterates rather than scaling once. Glyph metrics are only roughly linear in em size -
        /// hinting and rounding at small sizes make a single ratio overshoot - so this closes in,
        /// giving up after a few passes with whatever fits best.
        /// </remarks>
        private static Font FitFont(string text, Rectangle area, FontFamily family, float startEm)
        {
            float em = Math.Max(MinimumFontSize, startEm);
            Font font = new Font(family, em, FontStyle.Regular);

            if (string.IsNullOrEmpty(text) || area.Width <= 0 || area.Height <= 0)
            {
                return font;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                Size measured = TextRenderer.MeasureText(
                    text, font, new Size(int.MaxValue, int.MaxValue), FitFlags);

                if ((measured.Width <= area.Width && measured.Height <= area.Height)
                    || em <= MinimumFontSize)
                {
                    return font;
                }

                float scale = Math.Min(
                    area.Width / (float)Math.Max(1, measured.Width),
                    area.Height / (float)Math.Max(1, measured.Height));

                // Slightly under the computed ratio, so a pass that lands a hair over does not need
                // another one.
                em = Math.Max(MinimumFontSize, em * scale * 0.96f);

                font.Dispose();
                font = new Font(family, em, FontStyle.Regular);
            }

            return font;
        }

        private void DrawTextGauge(Graphics graphics, Rectangle bounds, GaugeLayout gauge)
        {
            double? value = this.ValueOf(gauge);

            using (Pen border = new Pen(AppTheme.Line))
            {
                graphics.DrawRectangle(border, bounds);
            }

            int titleHeight = Math.Max(12, bounds.Height / 4);
            Rectangle titleArea = new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, titleHeight);

            // A couple of pixels in from the border, so text never touches it.
            Rectangle valueArea = new Rectangle(
                bounds.X + 3, bounds.Y + titleHeight, bounds.Width - 6, bounds.Height - titleHeight - 1);

            using (Font titleFont = FitFont(gauge.Title, titleArea, this.Font.FontFamily, this.Font.Size))
            {
                TextRenderer.DrawText(
                    graphics, gauge.Title, titleFont, titleArea, AppTheme.Text,
                    FitFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }

            string text = value == null ? "--" : value.Value.ToString("F" + Math.Max(0, gauge.Digits));
            using (Font valueFont = FitFont(text, valueArea, this.Font.FontFamily, valueArea.Height * 0.55f))
            {
                TextRenderer.DrawText(
                    graphics, text, valueFont, valueArea, this.ColorFor(gauge, value),
                    FitFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis);
            }
        }

        private void DrawRoundGauge(Graphics graphics, Rectangle bounds, GaugeLayout gauge)
        {
            double? value = this.ValueOf(gauge);

            // Keep the dial circular inside whatever rectangle the layout gives it.
            int diameter = Math.Min(bounds.Width, bounds.Height);
            Rectangle dial = new Rectangle(
                bounds.X + ((bounds.Width - diameter) / 2),
                bounds.Y + ((bounds.Height - diameter) / 2),
                diameter,
                diameter);

            // The sweep is centred on straight down, which is where a dial's gap belongs.
            float sweep = Math.Max(30, Math.Min(350, gauge.ArcDegrees));
            float startAngle = 90f + ((360f - sweep) / 2f);

            using (Pen face = new Pen(AppTheme.Line, 2f))
            {
                graphics.DrawArc(face, dial, startAngle, sweep);
            }

            // The alarm band, drawn on the face so an out-of-range reading is obvious at a glance.
            if (gauge.HasAlarms && gauge.RangeHigh > gauge.RangeLow && gauge.AlarmHigh < gauge.RangeHigh)
            {
                float alarmStart = (float)((gauge.AlarmHigh - gauge.RangeLow) / (gauge.RangeHigh - gauge.RangeLow));
                using (Pen red = new Pen(Color.Red, 3f))
                {
                    graphics.DrawArc(red, dial, startAngle + (sweep * alarmStart), sweep * (1f - alarmStart));
                }
            }

            PointF centre = new PointF(dial.X + (dial.Width / 2f), dial.Y + (dial.Height / 2f));
            float radius = dial.Width / 2f;

            if (value != null && gauge.RangeHigh > gauge.RangeLow)
            {
                double fraction = (value.Value - gauge.RangeLow) / (gauge.RangeHigh - gauge.RangeLow);
                fraction = Math.Max(0, Math.Min(1, fraction));
                double angle = (startAngle + (sweep * fraction)) * Math.PI / 180.0;

                using (Pen needle = new Pen(AppTheme.Highlight, 2f))
                {
                    graphics.DrawLine(
                        needle,
                        centre,
                        new PointF(
                            centre.X + (float)(Math.Cos(angle) * radius * 0.8),
                            centre.Y + (float)(Math.Sin(angle) * radius * 0.8)));
                }
            }

            using (Brush hub = new SolidBrush(AppTheme.Line))
            {
                graphics.FillEllipse(hub, centre.X - 3, centre.Y - 3, 6, 6);
            }

            // Inset from the dial edge: the face is a circle, so text spanning its full width would
            // run outside the arc at top and bottom.
            int textWidth = (int)(dial.Width * 0.7f);
            int textLeft = dial.X + ((dial.Width - textWidth) / 2);

            Rectangle titleArea = new Rectangle(textLeft, (int)centre.Y - 24, textWidth, 16);
            using (Font titleFont = FitFont(gauge.Title, titleArea, this.Font.FontFamily, this.Font.Size))
            {
                TextRenderer.DrawText(
                    graphics, gauge.Title, titleFont, titleArea, AppTheme.Text,
                    FitFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }

            string text = value == null ? "--" : value.Value.ToString("F" + Math.Max(0, gauge.Digits));
            Rectangle valueArea = new Rectangle(textLeft, (int)centre.Y + 14, textWidth, 18);
            using (Font valueFont = FitFont(text, valueArea, this.Font.FontFamily, this.Font.Size))
            {
                TextRenderer.DrawText(
                    graphics, text, valueFont, valueArea, this.ColorFor(gauge, value),
                    FitFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }

            Rectangle unitsArea = new Rectangle(textLeft, (int)centre.Y + 30, textWidth, 16);
            using (Font unitsFont = FitFont(gauge.Units, unitsArea, this.Font.FontFamily, this.Font.Size))
            {
                TextRenderer.DrawText(
                    graphics, gauge.Units, unitsFont, unitsArea, AppTheme.MutedText,
                    FitFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
