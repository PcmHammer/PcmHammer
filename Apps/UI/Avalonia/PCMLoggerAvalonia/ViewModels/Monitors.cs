// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// One trace on a monitor: a parameter sampled over a moving time window and scaled to its configured
/// range. Built from the shared MonitorSeriesLayout. Samples are kept with timestamps so the chart can
/// scroll smoothly between readings.
/// </summary>
public sealed partial class MonitorSeries : ObservableObject
{
    // Keep a generous buffer so the visible window can be widened without having thrown samples away.
    private const double BufferSeconds = 600;

    private readonly List<(DateTime T, double V)> _points = new();
    private readonly double _rangeLow;
    private readonly double _rangeHigh;

    public string ParameterId { get; }
    public string Title { get; }
    public string Units { get; }
    public Color Color { get; }
    public IBrush Brush { get; }

    public string RangeText { get; }

    [ObservableProperty]
    private bool _visible = true;

    [ObservableProperty]
    private string _valueText = "--";

    public double LegendOpacity => this.Visible ? 1.0 : 0.35;

    public MonitorSeries(MonitorSeriesLayout layout, Color color)
        : this(layout.Title, layout.Units, color, layout.RangeLow, layout.RangeHigh, layout.PidId ?? string.Empty)
    {
    }

    /// <summary>A trace built directly (e.g. from a loaded log column) rather than from a layout.</summary>
    public MonitorSeries(string title, string units, Color color, double rangeLow, double rangeHigh, string? parameterId = null)
    {
        this.ParameterId = parameterId ?? title;
        this.Title = title;
        this.Units = units;
        _rangeLow = rangeLow;
        _rangeHigh = rangeHigh;
        this.Color = color;
        this.Brush = new SolidColorBrush(color);
        this.RangeText = string.Format(CultureInfo.CurrentCulture, "{0:0.##}..{1:0.##} {2}", _rangeLow, _rangeHigh, units);
    }

    partial void OnVisibleChanged(bool value) => this.OnPropertyChanged(nameof(LegendOpacity));

    /// <summary>Show or hide this trace (tapped from the legend).</summary>
    [RelayCommand]
    private void Toggle() => this.Visible = !this.Visible;

    public (double Low, double High) Range => (_rangeLow, _rangeHigh);

    public IReadOnlyList<(DateTime T, double V)> Points => _points;

    /// <summary>Record a sample now and drop anything older than the buffer.</summary>
    public void Append(double value)
    {
        DateTime now = DateTime.UtcNow;
        _points.Add((now, value));
        this.ValueText = value.ToString("0.##", CultureInfo.CurrentCulture);

        DateTime cutoff = now - TimeSpan.FromSeconds(BufferSeconds);
        int drop = 0;
        while (drop < _points.Count && _points[drop].T < cutoff)
        {
            drop++;
        }

        if (drop > 0)
        {
            _points.RemoveRange(0, drop);
        }
    }

    /// <summary>Discard all captured samples (Clear Log restarts the strip chart).</summary>
    public void Clear()
    {
        _points.Clear();
        this.ValueText = "--";
    }

    /// <summary>Replace the samples with a loaded set (log playback). All points are kept so the scrub
    /// bar can move anywhere in the log.</summary>
    public void SetPoints(IEnumerable<(DateTime T, double V)> points)
    {
        _points.Clear();
        _points.AddRange(points);
        this.ValueText = _points.Count > 0
            ? _points[_points.Count - 1].V.ToString("0.##", CultureInfo.CurrentCulture)
            : "--";
    }
}

/// <summary>One monitor (e.g. "Engine", "Fuelling"): a titled set of traces over a time window.</summary>
public sealed class MonitorTab
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6), // blue
        Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D), // red
        Color.FromArgb(0xFF, 0x30, 0xA4, 0x6A), // green
        Color.FromArgb(0xFF, 0xF5, 0xA6, 0x23), // orange
        Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA), // violet
        Color.FromArgb(0xFF, 0x22, 0xB8, 0xCF), // cyan
        Color.FromArgb(0xFF, 0xD6, 0x40, 0x9F), // magenta
        Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8), // slate
    };

    public string Title { get; }

    /// <summary>The visible time window (seconds); adjustable from the chart (mouse wheel).</summary>
    public double WindowSeconds { get; set; }

    public ObservableCollection<MonitorSeries> Series { get; } = new();

    public MonitorTab(MonitorLayout layout)
    {
        this.Title = layout.Title;
        this.WindowSeconds = layout.TimeSpanSeconds > 1 ? layout.TimeSpanSeconds : 20;

        int index = 0;
        foreach (MonitorSeriesLayout seriesLayout in layout.Series)
        {
            this.Series.Add(new MonitorSeries(seriesLayout, Palette[index % Palette.Length]));
            index++;
        }
    }

    /// <summary>An empty monitor built by hand (e.g. a log-playback tab filled from file columns).</summary>
    public MonitorTab(string title, double windowSeconds)
    {
        this.Title = title;
        this.WindowSeconds = windowSeconds;
    }

    /// <summary>Add a trace (used when building a playback tab from a loaded log's columns).</summary>
    public MonitorSeries AddSeries(string title, string units, double rangeLow, double rangeHigh)
    {
        MonitorSeries series = new(title, units, Palette[this.Series.Count % Palette.Length], rangeLow, rangeHigh);
        this.Series.Add(series);
        return series;
    }

    /// <summary>Clear every trace (Clear Log).</summary>
    public void Clear()
    {
        foreach (MonitorSeries series in this.Series)
        {
            series.Clear();
        }
    }

    public override string ToString() => this.Title;
}
