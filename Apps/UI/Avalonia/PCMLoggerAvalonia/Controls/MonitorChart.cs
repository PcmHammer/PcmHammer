// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Controls;

/// <summary>
/// A strip chart in the WinForms PcmLogger style: every trace shares one plot, and each gets its own
/// Y-axis column in its colour, stacked outward left and right of the plot, headed by the trace's name
/// and live value. One range (from the shared MonitorSeriesLayout) drives both the trace and its axis,
/// so two traces sharing a range overlay directly. Click an axis column to hide/show its trace.
/// </summary>
public sealed class MonitorChart : Control
{
    private const double EdgePadding = 6;
    private const double ColumnWidth = 50;
    private const double HeadingTop = 4;
    private const double LineHeight = 14;
    private const double BottomGutter = 18;

    private readonly DispatcherTimer _timer;

    // Axis column rectangles from the last render, so a click can find the trace it belongs to.
    private readonly List<(Rect Bounds, MonitorSeries Series)> _axisHits = new();

    private DateTime? _frozenNow;   // the time reference while paused, so the window stops scrolling
    private double? _cursorX;        // hovered x inside the plot, for the read-out cursor
    private Rect _plot;              // last plot rectangle, for hit-testing the cursor
    private double? _zoomStartX;     // rubber-band zoom (playback): drag start / current x
    private double? _zoomCurrentX;

    /// <summary>Playback drag-zoom: the selected region's right-edge time and width in seconds.</summary>
    public event Action<DateTime, double>? ZoomRequested;

    /// <summary>Playback double-click: fit the whole log back in view.</summary>
    public event Action? ResetZoomRequested;

    public MonitorChart()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => this.InvalidateVisual();

        // Double-click during playback fits the whole log.
        this.DoubleTapped += (_, _) =>
        {
            if (this.PlaybackNow != null)
            {
                this.ResetZoomRequested?.Invoke();
            }
        };
    }

    // A menu of every trace with a tick beside the shown ones (the WinForms way). A code-drawn control
    // does not get the automatic right-click ContextMenu, so it is shown explicitly from a right-click
    // (and the toolbar "Traces" button offers the same for environments that swallow right-click).
    private void ShowTraceFlyout()
    {
        MonitorTab? monitor = this.Monitor;
        if (monitor == null)
        {
            return;
        }

        MenuFlyout flyout = new();
        foreach (MonitorSeries series in monitor.Series)
        {
            MonitorSeries captured = series;
            MenuItem item = new() { Header = series.Title };
            if (series.Visible)
            {
                item.Icon = new TextBlock { Text = "✓", Foreground = new SolidColorBrush(series.Color) };
            }

            item.Click += (_, _) =>
            {
                captured.Visible = !captured.Visible;
                this.InvalidateVisual();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(this, showAtPointer: true);
    }

    public static readonly StyledProperty<MonitorTab?> MonitorProperty =
        AvaloniaProperty.Register<MonitorChart, MonitorTab?>(nameof(Monitor));

    public MonitorTab? Monitor
    {
        get => this.GetValue(MonitorProperty);
        set => this.SetValue(MonitorProperty, value);
    }

    public static readonly StyledProperty<bool> PausedProperty =
        AvaloniaProperty.Register<MonitorChart, bool>(nameof(Paused));

    /// <summary>When true the window stops scrolling (the view is frozen at the moment of pause).</summary>
    public bool Paused
    {
        get => this.GetValue(PausedProperty);
        set => this.SetValue(PausedProperty, value);
    }

    public static readonly StyledProperty<DateTime?> PlaybackNowProperty =
        AvaloniaProperty.Register<MonitorChart, DateTime?>(nameof(PlaybackNow));

    /// <summary>When set (log playback), the window's right edge is this time instead of real "now",
    /// so a scrub bar can move the view through a loaded log.</summary>
    public DateTime? PlaybackNow
    {
        get => this.GetValue(PlaybackNowProperty);
        set => this.SetValue(PlaybackNowProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MonitorProperty || change.Property == PlaybackNowProperty)
        {
            this.InvalidateVisual();
        }
        else if (change.Property == PausedProperty)
        {
            _frozenNow = this.Paused ? DateTime.UtcNow : null;
            this.InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        MonitorTab? monitor = this.Monitor;
        if (monitor == null)
        {
            return;
        }

        // Wheel up widens the window (more history), wheel down narrows it. Clamp to a useful range.
        double factor = e.Delta.Y > 0 ? 1.25 : 0.8;
        monitor.WindowSeconds = Math.Max(2, Math.Min(300, monitor.WindowSeconds * factor));
        this.InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);

        if (_zoomStartX != null)
        {
            _zoomCurrentX = p.X;
            _cursorX = null;
            this.InvalidateVisual();
            return;
        }

        _cursorX = (p.X >= _plot.Left && p.X <= _plot.Right && p.Y >= _plot.Top && p.Y <= _plot.Bottom)
            ? p.X
            : (double?)null;
        this.InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursorX = null;
        this.InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            // Show the trace menu on right-click; don't also run the axis-toggle.
            this.ShowTraceFlyout();
            e.Handled = true;
            return;
        }

        Point p = e.GetPosition(this);

        // Playback: a left-drag inside the plot rubber-bands a region to zoom into.
        if (this.PlaybackNow != null && _plot.Contains(p))
        {
            _zoomStartX = p.X;
            _zoomCurrentX = p.X;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Left-click an axis column to toggle its trace (the Traces button is the easier target).
        foreach ((Rect bounds, MonitorSeries series) in _axisHits)
        {
            if (bounds.Contains(p))
            {
                series.Visible = !series.Visible;
                this.InvalidateVisual();
                break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_zoomStartX == null || _zoomCurrentX == null)
        {
            return;
        }

        double x1 = _zoomStartX.Value;
        double x2 = _zoomCurrentX.Value;
        _zoomStartX = null;
        _zoomCurrentX = null;
        e.Pointer.Capture(null);
        this.InvalidateVisual();

        MonitorTab? monitor = this.Monitor;
        if (monitor == null || _plot.Width < 1 || Math.Abs(x2 - x1) < 5)
        {
            return;   // a click, or nothing to zoom
        }

        double window = monitor.WindowSeconds;
        DateTime now = this.PlaybackNow ?? _frozenNow ?? DateTime.UtcNow;

        DateTime TimeAtX(double x)
        {
            double clamped = Math.Max(_plot.Left, Math.Min(_plot.Right, x));
            double age = (_plot.Right - clamped) / _plot.Width * window;
            return now - TimeSpan.FromSeconds(age);
        }

        DateTime ta = TimeAtX(x1);
        DateTime tb = TimeAtX(x2);
        DateTime rightEdge = ta > tb ? ta : tb;
        double newWindow = Math.Abs((ta - tb).TotalSeconds);
        if (newWindow < 0.5)
        {
            newWindow = 0.5;
        }

        this.ZoomRequested?.Invoke(rightEdge, newWindow);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _axisHits.Clear();

        double w = this.Bounds.Width;
        double h = this.Bounds.Height;

        // Fill the whole area (transparent) so the entire chart is hit-testable. A code-drawn control
        // otherwise only receives pointer events where it actually painted a line, which is why
        // clicks and right-clicks only worked when the pointer was exactly on a trace.
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));

        MonitorTab? monitor = this.Monitor;
        if (monitor == null || w < 40 || h < 40)
        {
            return;
        }

        // Only visible traces get an axis column, so hiding a trace frees its space (otherwise a log
        // with many columns stacks a cramped wall of axes). Hidden traces stay in the Traces menu.
        IReadOnlyList<MonitorSeries> all = monitor.Series.Where(s => s.Visible).ToList();
        int n = all.Count;

        bool dark = this.ActualThemeVariant == ThemeVariant.Dark;
        IBrush ink = new SolidColorBrush(dark ? Color.FromArgb(0xFF, 0xB8, 0xB8, 0xB8) : Color.FromArgb(0xFF, 0x50, 0x50, 0x50));
        IBrush muted = new SolidColorBrush(dark ? Color.FromArgb(0xFF, 0x80, 0x80, 0x80) : Color.FromArgb(0xFF, 0x90, 0x90, 0x90));
        Pen framePen = new(new SolidColorBrush(dark ? Color.FromArgb(0x66, 0x9A, 0x9A, 0x9A) : Color.FromArgb(0x66, 0x88, 0x88, 0x88)), 1);

        // Axes split left/right, the left taking the extra one, and stack outward from the plot.
        int leftCount = (n + 1) / 2;
        int rightCount = n / 2;
        double plotLeft = EdgePadding + (leftCount * ColumnWidth);
        double plotRight = w - EdgePadding - (rightCount * ColumnWidth);
        double plotTop = HeadingTop + (LineHeight * 2) + 4;
        double plotBottom = h - BottomGutter;
        Rect plot = new(plotLeft, plotTop, Math.Max(1, plotRight - plotLeft), Math.Max(1, plotBottom - plotTop));
        _plot = plot;

        context.DrawRectangle(null, framePen, plot);

        // Title (monitor name) over the plot, clear of the axis-column headings on either side.
        string title = this.Paused ? monitor.Title + "   (stopped)" : monitor.Title;
        context.DrawText(
            new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, ink),
            new Point(plot.Left + 4, 1));

        double window = monitor.WindowSeconds;
        DateTime now = this.PlaybackNow ?? _frozenNow ?? DateTime.UtcNow;

        for (int index = 0; index < n; index++)
        {
            MonitorSeries series = all[index];
            (double low, double high) = series.Range;
            double span = high - low;
            if (span <= 1e-9)
            {
                span = 1;
            }

            Color color = series.Color;
            IBrush colorBrush = new SolidColorBrush(color);
            double dim = series.Visible ? 1.0 : 0.35;

            // Where this trace's axis column sits.
            bool onLeft = index < leftCount;
            int slot = onLeft ? index : index - leftCount;
            double axisX = onLeft
                ? plot.Left - ((slot + 1) * ColumnWidth)
                : plot.Right + (slot * ColumnWidth);
            Rect axis = new(axisX, HeadingTop, ColumnWidth, plot.Bottom - HeadingTop);
            _axisHits.Add((axis, series));

            // The trace, scaled to its range (visible only).
            if (series.Visible && series.Points.Count > 1)
            {
                StreamGeometry geometry = new();
                using (StreamGeometryContext gc = geometry.Open())
                {
                    bool started = false;
                    foreach ((DateTime t, double v) in series.Points)
                    {
                        double age = (now - t).TotalSeconds;
                        if (age > window || age < 0)
                        {
                            continue;
                        }

                        double x = plot.Right - ((age / window) * plot.Width);
                        double norm = Math.Max(0, Math.Min(1, (v - low) / span));
                        double y = plot.Bottom - (norm * plot.Height);
                        Point pt = new(x, y);
                        if (!started)
                        {
                            gc.BeginFigure(pt, false);
                            started = true;
                        }
                        else
                        {
                            gc.LineTo(pt);
                        }
                    }

                    if (started)
                    {
                        gc.EndFigure(false);
                    }
                }

                context.DrawGeometry(null, new Pen(colorBrush, 1.5), geometry);
            }

            // The axis rule, ticks and labels, in the trace's colour.
            IBrush axisBrush = series.Visible ? colorBrush : new SolidColorBrush(color, dim);
            Pen axisPen = new(axisBrush, 1);
            double ruleX = onLeft ? axis.Right - 1 : axis.Left;
            context.DrawLine(axisPen, new Point(ruleX, plot.Top), new Point(ruleX, plot.Bottom));

            int ticks = Math.Max(2, Math.Min(6, (int)(plot.Height / 34)));
            int digits = span >= 50 ? 0 : span >= 5 ? 1 : 2;
            for (int ti = 0; ti < ticks; ti++)
            {
                double fraction = ti / (double)(ticks - 1);
                double y = plot.Bottom - (fraction * plot.Height);
                double mark = onLeft ? -4 : 4;
                context.DrawLine(axisPen, new Point(ruleX, y), new Point(ruleX + mark, y));

                string tickText = (low + ((high - low) * fraction)).ToString("F" + digits, CultureInfo.CurrentCulture);
                FormattedText label = new(tickText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, axisBrush);
                double labelY = Math.Max(plot.Top, Math.Min(plot.Bottom - 11, y - 6));
                double labelX = onLeft ? axis.Right - 6 - label.Width : axis.Left + 6;
                context.DrawText(label, new Point(labelX, labelY));
            }

            // Heading: trace name then its live value, centred over the column, in its colour.
            DrawCentered(context, series.Title, 10, axisBrush, axis.X, axis.Width, HeadingTop);
            DrawCentered(context, series.ValueText, 10, axisBrush, axis.X, axis.Width, HeadingTop + LineHeight);
        }

        // Time axis: a span label centred under the plot.
        string spanLabel = window >= 60
            ? (window / 60).ToString("0.#", CultureInfo.CurrentCulture) + " min window"
            : window.ToString("0.#", CultureInfo.CurrentCulture) + " s window";
        FormattedText spanText = new(spanLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, muted);
        context.DrawText(spanText, new Point(plot.Left + ((plot.Width - spanText.Width) / 2), plot.Bottom + 3));
        context.DrawText(
            new FormattedText("now", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, muted),
            new Point(plot.Right - 22, plot.Bottom + 3));

        // Hover cursor: a dotted vertical line, and each visible trace's value where the line crosses it.
        if (_cursorX is double cx && cx >= plot.Left && cx <= plot.Right)
        {
            Pen cursorPen = new(ink, 1) { DashStyle = DashStyle.Dash };
            context.DrawLine(cursorPen, new Point(cx, plot.Top), new Point(cx, plot.Bottom));

            double cursorAge = (plot.Right - cx) / plot.Width * window; // seconds before 'now'
            DateTime cursorTime = now - TimeSpan.FromSeconds(cursorAge);

            foreach (MonitorSeries series in all)
            {
                if (!series.Visible || series.Points.Count == 0)
                {
                    continue;
                }

                double bestDelta = double.MaxValue;
                double bestValue = double.NaN;
                foreach ((DateTime t, double v) in series.Points)
                {
                    double d = Math.Abs((t - cursorTime).TotalSeconds);
                    if (d < bestDelta)
                    {
                        bestDelta = d;
                        bestValue = v;
                    }
                }

                if (double.IsNaN(bestValue) || bestDelta > 1.5)
                {
                    continue;
                }

                (double low, double high) = series.Range;
                double vspan = high - low;
                if (vspan <= 1e-9)
                {
                    vspan = 1;
                }

                double norm = Math.Max(0, Math.Min(1, (bestValue - low) / vspan));
                double y = plot.Bottom - (norm * plot.Height);
                IBrush b = new SolidColorBrush(series.Color);
                FormattedText vt = new(bestValue.ToString("0.##", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, b);
                double tx = cx + 4;
                if (tx + vt.Width > plot.Right)
                {
                    tx = cx - 4 - vt.Width;
                }

                double ty = Math.Max(plot.Top, Math.Min(plot.Bottom - 12, y - 14));
                context.DrawText(vt, new Point(tx, ty));
            }
        }

        // Rubber-band zoom selection (playback drag).
        if (_zoomStartX != null && _zoomCurrentX != null)
        {
            double xa = Math.Max(plot.Left, Math.Min(_zoomStartX.Value, _zoomCurrentX.Value));
            double xb = Math.Min(plot.Right, Math.Max(_zoomStartX.Value, _zoomCurrentX.Value));
            if (xb > xa)
            {
                Rect selection = new(xa, plot.Top, xb - xa, plot.Height);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x33, 0x66, 0x99, 0xFF)), selection);
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x66, 0x99, 0xFF)), 1), selection);
            }
        }
    }

    private static void DrawCentered(DrawingContext context, string text, double size, IBrush brush, double x, double width, double y)
    {
        FormattedText ft = new(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush)
        {
            MaxTextWidth = width,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(ft, new Point(x, y));
    }
}
