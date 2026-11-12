// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Controls;

/// <summary>
/// A horizontal bar for one <see cref="GaugeTile"/>: a rounded track with a fill proportional to the
/// value, any alarm region tinted on the track, the min/max at the ends and the value above. The sister
/// of <see cref="GaugeDial"/> for the Bar gauge kind. Drawn with a DrawingContext.
/// </summary>
public sealed class BarGauge : Control
{
    public static readonly StyledProperty<GaugeTile?> TileProperty =
        AvaloniaProperty.Register<BarGauge, GaugeTile?>(nameof(Tile));

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

            this.InvalidateVisual();
        }
        else if (change.Property == BoundsProperty)
        {
            this.InvalidateVisual();
        }
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e) => this.InvalidateVisual();

    // A bare Control measures to nothing by default, which would leave it zero-sized in a Grid cell and
    // draw nothing; report the available size so it fills its slot like the dial does.
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 80 : availableSize.Height;
        return new Size(w, h);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        GaugeTile? tile = this.Tile;
        double w = this.Bounds.Width;
        double h = this.Bounds.Height;
        if (tile == null || w < 20 || h < 20)
        {
            return;
        }

        bool dark = this.ActualThemeVariant == ThemeVariant.Dark;
        Color trackColor = dark ? Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A) : Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8);
        Color inkColor = dark ? Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8) : Color.FromArgb(0xFF, 0x50, 0x50, 0x50);
        IBrush inkBrush = new SolidColorBrush(inkColor);

        double pad = 12;
        double left = pad;
        double right = w - pad;
        double trackW = Math.Max(1, right - left);
        double trackH = Math.Max(10, Math.Min(26, h * 0.16));
        double cy = h * 0.52;
        Rect track = new(left, cy - (trackH / 2), trackW, trackH);
        double radius = trackH / 2;

        context.DrawRectangle(new SolidColorBrush(trackColor), null, new RoundedRect(track, radius));

        double min = tile.Min;
        double max = tile.Max;
        double span = max - min;
        Color alarmColor = tile.AlarmColor;

        if (span > 1e-9)
        {
            if (tile.AlarmHigh is double high && high > min && high < max)
            {
                double x = left + ((high - min) / span * trackW);
                context.DrawRectangle(new SolidColorBrush(alarmColor, 0.45), null, new RoundedRect(new Rect(x, track.Y, right - x, trackH), radius));
            }

            if (tile.AlarmLow is double low && low > min && low < max)
            {
                double x = left + ((low - min) / span * trackW);
                context.DrawRectangle(new SolidColorBrush(alarmColor, 0.45), null, new RoundedRect(new Rect(left, track.Y, x - left, trackH), radius));
            }
        }

        bool hasValue = tile.ValueText != "--";
        Color accent = tile.InAlarm ? tile.AlarmColor : tile.NormalColor;

        if (hasValue)
        {
            double fillW = Math.Max(0, Math.Min(1, tile.Fraction)) * trackW;
            if (fillW > 1)
            {
                context.DrawRectangle(new SolidColorBrush(accent), null, new RoundedRect(new Rect(left, track.Y, fillW, trackH), radius));
            }
        }

        // Min/max at the ends, under the track.
        FormattedText minText = new(min.ToString("0.#", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, inkBrush);
        FormattedText maxText = new(max.ToString("0.#", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, inkBrush);
        context.DrawText(minText, new Point(left, track.Bottom + 3));
        context.DrawText(maxText, new Point(right - maxText.Width, track.Bottom + 3));

        // Value (and units) above the track, centred, in the accent colour.
        Typeface bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        FormattedText value = new(tile.ValueText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold, Math.Max(16, h * 0.17), new SolidColorBrush(accent));
        FormattedText units = new(tile.Units ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, inkBrush);
        double totalW = value.Width + 4 + units.Width;
        double startX = left + ((trackW - totalW) / 2);
        double textY = track.Y - value.Height - 4;
        context.DrawText(value, new Point(startX, textY));
        context.DrawText(units, new Point(startX + value.Width + 4, textY + (value.Height - units.Height)));
    }
}
