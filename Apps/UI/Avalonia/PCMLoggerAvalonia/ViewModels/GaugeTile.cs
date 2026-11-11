// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmHacking;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// One live gauge on the dashboard, built from a shared <see cref="GaugeLayout"/> (the same data the
/// WinForms PcmLogger dashboard uses, and what a TunerPro ADX import produces). Carries the layout's
/// kind, proportional position, range, alarms and colours, plus the latest value. The GaugeDial draws
/// a dial from it; a text gauge binds to its value/title directly.
/// </summary>
public sealed class GaugeTile : ObservableObject
{
    public static readonly Color DefaultNormalColor = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);

    private readonly int _digits;
    private double _currentValue;
    private bool _hasValue;

    public string ParameterId { get; }
    public GaugeKind Kind { get; }

    // Proportional bounds within the dashboard (0..1), straight from the layout - the dashboard panel
    // scales these to the current size, exactly as the WinForms dashboard does.
    public double Left { get; }
    public double Top { get; }
    public double RelWidth { get; }
    public double RelHeight { get; }

    public bool IsDial => this.Kind == GaugeKind.Round;
    public bool IsText => this.Kind == GaugeKind.Text || this.Kind == GaugeKind.Indicator;
    public bool IsBar => this.Kind == GaugeKind.Bar;

    public GaugeTile(GaugeLayout layout)
    {
        this.ParameterId = layout.PidId ?? string.Empty;
        this.Kind = layout.Kind;
        _title = layout.Title;
        _units = layout.Units;
        _digits = layout.Digits;

        _min = layout.RangeLow;
        _max = layout.RangeHigh;

        // Only treat an alarm edge as real when it sits inside the range; equal-to-bound means "no
        // alarm that side" (WinForms defaults the unused edge to the range bound).
        _alarmLow = layout.HasAlarms && layout.AlarmLow > layout.RangeLow ? layout.AlarmLow : null;
        _alarmHigh = layout.HasAlarms && layout.AlarmHigh < layout.RangeHigh ? layout.AlarmHigh : null;

        _alarmColor = FromRgb(layout.AlarmColor, Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D));
        _normalColor = layout.NormalColor == 0 ? DefaultNormalColor : FromRgb(layout.NormalColor, DefaultNormalColor);

        this.Left = layout.Left;
        this.Top = layout.Top;
        this.RelWidth = layout.Width;
        this.RelHeight = layout.Height;
    }

    private static Color FromRgb(int rgb, Color fallback)
    {
        if (rgb == 0)
        {
            return fallback;
        }

        return Color.FromArgb(0xFF, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
    }

    private string _title;
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    private string _units;
    public string Units { get => _units; set => SetProperty(ref _units, value); }

    private double _min;
    public double Min { get => _min; private set { if (SetProperty(ref _min, value)) { Recompute(); } } }

    private double _max;
    public double Max { get => _max; private set { if (SetProperty(ref _max, value)) { Recompute(); } } }

    private double? _alarmLow;
    public double? AlarmLow { get => _alarmLow; private set { if (SetProperty(ref _alarmLow, value)) { Recompute(); } } }

    private double? _alarmHigh;
    public double? AlarmHigh { get => _alarmHigh; private set { if (SetProperty(ref _alarmHigh, value)) { Recompute(); } } }

    private Color _normalColor;
    public Color NormalColor { get => _normalColor; private set => SetProperty(ref _normalColor, value); }

    private Color _alarmColor;
    public Color AlarmColor { get => _alarmColor; private set => SetProperty(ref _alarmColor, value); }

    private bool _inAlarm;
    public bool InAlarm { get => _inAlarm; private set { if (SetProperty(ref _inAlarm, value)) { OnPropertyChanged(nameof(ValueBrush)); } } }

    private string _valueText = "--";
    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }

    private double _fraction;
    public double Fraction { get => _fraction; private set => SetProperty(ref _fraction, value); }

    /// <summary>The colour for a text gauge's number: the alarm colour while in alarm, else the normal colour.</summary>
    public IBrush ValueBrush => new SolidColorBrush(_inAlarm ? _alarmColor : _normalColor);

    /// <summary>Apply the latest numeric reading and refresh the visual.</summary>
    public void SetValue(double value)
    {
        _currentValue = value;
        _hasValue = true;
        Recompute();
    }

    private void Recompute()
    {
        if (!_hasValue)
        {
            this.ValueText = "--";
            this.Fraction = 0;
            return;
        }

        this.InAlarm = (_alarmLow.HasValue && _currentValue < _alarmLow.Value)
            || (_alarmHigh.HasValue && _currentValue > _alarmHigh.Value);

        double span = _max - _min;
        double f = span > 1e-6 ? (_currentValue - _min) / span : 0;
        this.Fraction = Math.Max(0, Math.Min(1, f));
        this.ValueText = _currentValue.ToString("F" + _digits, CultureInfo.CurrentCulture);
    }
}
