// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
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

    public MonitorChart()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => this.InvalidateVisual();
    }

    public static readonly StyledProperty<MonitorTab?> MonitorProperty =
        AvaloniaProperty.Register<MonitorChart, MonitorTab?>(nameof(Monitor));

    public MonitorTab? Monitor
    {
        get => this.GetValue(MonitorProperty);
        set => this.SetValue(MonitorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MonitorProperty)
        {
            this.InvalidateVisual();
        }
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
        Point p = e.GetPosition(this);
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _axisHits.Clear();

        MonitorTab? monitor = this.Monitor;
        double w = this.Bounds.Width;
        double h = this.Bounds.Height;
        if (monitor == null || w < 40 || h < 40)
        {
            return;
        }

        IReadOnlyList<MonitorSeries> all = monitor.Series;
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

        context.DrawRectangle(null, framePen, plot);

        // Title (monitor name) top-left.
        context.DrawText(
            new FormattedText(monitor.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, ink),
            new Point(EdgePadding, 1));

        double window = monitor.WindowSeconds;
        DateTime now = DateTime.UtcNow;

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
