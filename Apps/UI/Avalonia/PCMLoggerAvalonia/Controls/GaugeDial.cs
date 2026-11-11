// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using PCMLoggerAvalonia.ViewModels;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace PCMLoggerAvalonia.Controls;

/// <summary>
/// A round, automotive-style dial for one <see cref="GaugeTile"/>: a 270-degree scale with tick marks
/// and numbers, a red arc over any alarm (redline) region, a needle pointing at the current value, and
/// the value and units in the centre. Drawn in code onto a Canvas so the ticks and labels can follow
/// the gauge's range. Ported from the Uno PcmLogger dashboard to Avalonia.
/// </summary>
public sealed class GaugeDial : UserControl
{
    private const double StartAngleDeg = 135.0;
    private const double SweepDeg = 270.0;

    private readonly Canvas _canvas = new();
    private Line? _needle;
    private Ellipse? _hub;
    private TextBlock? _valueText;
    private TextBlock? _unitsText;

    private double _drawnMin = double.NaN;
    private double _drawnMax = double.NaN;
    private double? _drawnAlarmLow;
    private double? _drawnAlarmHigh;
    private string _drawnUnits = string.Empty;
    private double _drawnWidth;
    private double _drawnHeight;

    public GaugeDial()
    {
        this.Content = _canvas;
        this.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        this.VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public static readonly StyledProperty<GaugeTile?> TileProperty =
        AvaloniaProperty.Register<GaugeDial, GaugeTile?>(nameof(Tile));

    public GaugeTile? Tile
    {
        get => this.GetValue(TileProperty);
        set => this.SetValue(TileProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TileProperty)
        {
            if (change.OldValue is GaugeTile oldTile)
            {
                oldTile.PropertyChanged -= this.OnTilePropertyChanged;
            }

            if (change.NewValue is GaugeTile newTile)
            {
                newTile.PropertyChanged += this.OnTilePropertyChanged;
            }

            _drawnWidth = 0;
            this.RebuildStatic();
        }
        else if (change.Property == BoundsProperty)
        {
            this.RebuildStatic();
        }
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GaugeTile.Min):
            case nameof(GaugeTile.Max):
            case nameof(GaugeTile.AlarmLow):
            case nameof(GaugeTile.AlarmHigh):
            case nameof(GaugeTile.Units):
                this.RebuildStatic();
                break;

            case nameof(GaugeTile.Fraction):
            case nameof(GaugeTile.ValueText):
            case nameof(GaugeTile.InAlarm):
                this.UpdateDynamic();
                break;
        }
    }

    private Point PointAt(double cx, double cy, double r, double frac)
    {
        double angle = (StartAngleDeg + (frac * SweepDeg)) * Math.PI / 180.0;
        return new Point(cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
    }

    private PathGeometry BuildArc(double cx, double cy, double r, double fracA, double fracB)
    {
        PathGeometry geometry = new();
        if (fracB <= fracA)
        {
            return geometry;
        }

        PathFigure figure = new() { StartPoint = this.PointAt(cx, cy, r, fracA), IsClosed = false };
        figure.Segments!.Add(new ArcSegment
        {
            Point = this.PointAt(cx, cy, r, fracB),
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = (fracB - fracA) * SweepDeg > 180.0,
        });
        geometry.Figures!.Add(figure);
        return geometry;
    }

    /// <summary>Draw the fixed parts: the track, redline, ticks, numbers and the needle/value fields.</summary>
    private void RebuildStatic()
    {
        GaugeTile? tile = this.Tile;
        double w = this.Bounds.Width;
        double h = this.Bounds.Height;
        if (tile == null || w < 2 || h < 2)
        {
            return;
        }

        _canvas.Children.Clear();
        _canvas.Width = w;
        _canvas.Height = h;

        double size = Math.Min(w, h);
        double cx = w / 2;
        double cy = h / 2;
        double radius = size * 0.40;
        double thickness = Math.Max(4, size * 0.055);

        bool dark = this.ActualThemeVariant == ThemeVariant.Dark;
        Color trackColor = dark ? Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A) : Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8);
        Color inkColor = dark ? Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0) : Color.FromArgb(0xFF, 0x40, 0x40, 0x40);
        Color alarmColor = tile.AlarmColor;

        // Background track, the full 270 degrees.
        _canvas.Children.Add(new ShapePath
        {
            Data = this.BuildArc(cx, cy, radius, 0, 1),
            Stroke = new SolidColorBrush(trackColor),
            StrokeThickness = thickness,
        });

        double min = tile.Min;
        double max = tile.Max;
        double span = max - min;

        if (span > 1e-9)
        {
            if (tile.AlarmHigh is double high && high > min && high < max)
            {
                double f = (high - min) / span;
                _canvas.Children.Add(new ShapePath
                {
                    Data = this.BuildArc(cx, cy, radius, f, 1),
                    Stroke = new SolidColorBrush(alarmColor),
                    StrokeThickness = thickness,
                });
            }

            if (tile.AlarmLow is double low && low > min && low < max)
            {
                double f = (low - min) / span;
                _canvas.Children.Add(new ShapePath
                {
                    Data = this.BuildArc(cx, cy, radius, 0, f),
                    Stroke = new SolidColorBrush(alarmColor),
                    StrokeThickness = thickness,
                });
            }
        }

        if (span > 1e-9)
        {
            double step = NiceStep(span, 5);
            int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step)));
            double tickOuter = radius - (thickness / 2) - (size * 0.015);
            double majorInner = radius - (thickness / 2) - (size * 0.075);
            double minorInner = radius - (thickness / 2) - (size * 0.045);
            double labelRadius = radius - (thickness / 2) - (size * 0.185);
            SolidColorBrush ink = new(inkColor);

            double minorStep = step / 2;
            double firstMinor = Math.Ceiling(min / minorStep) * minorStep;
            for (double v = firstMinor; v <= max + (minorStep * 1e-6); v += minorStep)
            {
                double f = (v - min) / span;
                if (f < -1e-6 || f > 1 + 1e-6)
                {
                    continue;
                }

                Point o = this.PointAt(cx, cy, tickOuter, f);
                Point i = this.PointAt(cx, cy, minorInner, f);
                _canvas.Children.Add(new Line
                {
                    StartPoint = o, EndPoint = i,
                    Stroke = ink, StrokeThickness = Math.Max(1, size * 0.008), Opacity = 0.6,
                });
            }

            double first = Math.Ceiling(min / step) * step;
            for (double v = first; v <= max + (step * 1e-6); v += step)
            {
                double f = (v - min) / span;
                if (f < -1e-6 || f > 1 + 1e-6)
                {
                    continue;
                }

                Point outer = this.PointAt(cx, cy, tickOuter, f);
                Point inner = this.PointAt(cx, cy, majorInner, f);
                _canvas.Children.Add(new Line
                {
                    StartPoint = outer, EndPoint = inner,
                    Stroke = ink, StrokeThickness = Math.Max(1, size * 0.013),
                });

                TextBlock label = new()
                {
                    Text = v.ToString("F" + decimals, CultureInfo.CurrentCulture),
                    FontSize = Math.Max(7, size * 0.072),
                    Foreground = ink,
                };
                label.Measure(Size.Infinity);
                Point lp = this.PointAt(cx, cy, labelRadius, f);
                Canvas.SetLeft(label, lp.X - (label.DesiredSize.Width / 2));
                Canvas.SetTop(label, lp.Y - (label.DesiredSize.Height / 2));
                _canvas.Children.Add(label);
            }
        }

        _needle = new Line { StrokeThickness = Math.Max(2, size * 0.022) };
        _canvas.Children.Add(_needle);

        double hubRadius = size * 0.045;
        _hub = new Ellipse
        {
            Width = hubRadius * 2,
            Height = hubRadius * 2,
            Fill = new SolidColorBrush(inkColor),
        };
        Canvas.SetLeft(_hub, cx - hubRadius);
        Canvas.SetTop(_hub, cy - hubRadius);
        _canvas.Children.Add(_hub);

        _valueText = new TextBlock
        {
            FontSize = Math.Max(12, size * 0.15),
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(inkColor),
        };
        _canvas.Children.Add(_valueText);

        _unitsText = new TextBlock
        {
            Text = tile.Units ?? string.Empty,
            FontSize = Math.Max(8, size * 0.075),
            Opacity = 0.75,
            Foreground = new SolidColorBrush(inkColor),
        };
        _canvas.Children.Add(_unitsText);

        _drawnMin = min;
        _drawnMax = max;
        _drawnAlarmLow = tile.AlarmLow;
        _drawnAlarmHigh = tile.AlarmHigh;
        _drawnUnits = tile.Units ?? string.Empty;
        _drawnWidth = w;
        _drawnHeight = h;

        this.UpdateDynamic();
    }

    /// <summary>Move the needle and refresh the value text/colour for the latest reading.</summary>
    private void UpdateDynamic()
    {
        GaugeTile? tile = this.Tile;
        if (tile == null || _needle == null || _valueText == null || _unitsText == null)
        {
            return;
        }

        double w = this.Bounds.Width;
        double h = this.Bounds.Height;
        if (_drawnWidth != w || _drawnHeight != h
            || _drawnMin != tile.Min || _drawnMax != tile.Max
            || _drawnAlarmLow != tile.AlarmLow || _drawnAlarmHigh != tile.AlarmHigh
            || _drawnUnits != (tile.Units ?? string.Empty))
        {
            this.RebuildStatic();
            return;
        }

        double size = Math.Min(w, h);
        double cx = w / 2;
        double cy = h / 2;
        double needleLength = size * 0.36;

        bool hasValue = tile.ValueText != "--";
        Color needleColor = tile.InAlarm ? tile.AlarmColor : tile.NormalColor;
        _needle.Stroke = new SolidColorBrush(needleColor);
        _needle.IsVisible = hasValue;

        Point tip = this.PointAt(cx, cy, needleLength, tile.Fraction);
        _needle.StartPoint = new Point(cx, cy);
        _needle.EndPoint = tip;

        _valueText.Text = tile.ValueText;
        _valueText.Foreground = new SolidColorBrush(needleColor);
        _valueText.Measure(Size.Infinity);
        Canvas.SetLeft(_valueText, cx - (_valueText.DesiredSize.Width / 2));
        Canvas.SetTop(_valueText, cy + (size * 0.20));

        _unitsText.Text = tile.Units ?? string.Empty;
        _unitsText.Measure(Size.Infinity);
        Canvas.SetLeft(_unitsText, cx - (_unitsText.DesiredSize.Width / 2));
        Canvas.SetTop(_unitsText, cy + (size * 0.20) + _valueText.DesiredSize.Height);
    }

    /// <summary>A "nice" tick step (1/2/5 x 10^n) giving roughly <paramref name="targetCount"/> ticks.</summary>
    private static double NiceStep(double range, int targetCount)
    {
        if (range <= 0 || targetCount <= 0)
        {
            return 1;
        }

        double raw = range / targetCount;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;
        double niceNormalized = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return niceNormalized * magnitude;
    }
}
