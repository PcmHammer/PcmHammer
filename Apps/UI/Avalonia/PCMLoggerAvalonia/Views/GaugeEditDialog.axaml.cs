using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PcmHacking;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Views;

/// <summary>
/// A small modal dialog to edit one gauge's parameter, title, range, alarms and style. Writes straight
/// back to the GaugeTile on OK, which the dashboard rebinds to live.
/// </summary>
public partial class GaugeEditDialog : Window
{
    // The default dashboard's gauges keyed by parameter id, so picking a known parameter can seed the
    // title, range, alarms and style. Built from the same shared package the dashboard uses.
    private static readonly Dictionary<string, GaugeLayout> DefaultGauges = BuildDefaults();

    private readonly GaugeTile? _tile;
    private bool _ready;

    public GaugeEditDialog()
    {
        InitializeComponent();
    }

    public GaugeEditDialog(GaugeTile tile, IReadOnlyList<ParameterOption> availableParameters)
        : this()
    {
        _tile = tile;

        this.ParameterBox.ItemsSource = availableParameters;
        this.ParameterBox.SelectedItem = availableParameters.FirstOrDefault(p => p.Id == tile.ParameterId);

        this.TitleBox.Text = tile.Title;
        this.MinBox.Text = tile.Min.ToString(CultureInfo.CurrentCulture);
        this.MaxBox.Text = tile.Max.ToString(CultureInfo.CurrentCulture);
        this.AlarmLowBox.Text = tile.AlarmLow?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        this.AlarmHighBox.Text = tile.AlarmHigh?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

        this.DialRadio.IsChecked = tile.Kind == GaugeKind.Round;
        this.BarRadio.IsChecked = tile.Kind == GaugeKind.Bar;
        this.TextRadio.IsChecked = tile.Kind != GaugeKind.Round && tile.Kind != GaugeKind.Bar;

        // Only after the fields are seeded, so the initial selection does not overwrite the tile's values.
        _ready = true;
        this.ParameterBox.SelectionChanged += this.ParameterBox_SelectionChanged;
    }

    private static Dictionary<string, GaugeLayout> BuildDefaults()
    {
        Dictionary<string, GaugeLayout> map = new();
        DashboardLayout? dashboard = DefaultLoggerPackage.Create().Dashboards.FirstOrDefault();
        if (dashboard != null)
        {
            foreach (GaugeLayout gauge in dashboard.Gauges)
            {
                if (!string.IsNullOrEmpty(gauge.PidId))
                {
                    map[gauge.PidId!] = gauge;
                }
            }
        }

        return map;
    }

    // Picking a different parameter re-seeds the title, range, alarms and style from its default gauge
    // (for a parameter the default dashboard knows); otherwise just the title, leaving the range to edit.
    private void ParameterBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || this.ParameterBox.SelectedItem is not ParameterOption chosen)
        {
            return;
        }

        if (DefaultGauges.TryGetValue(chosen.Id, out GaugeLayout? layout))
        {
            this.TitleBox.Text = layout.Title;
            this.MinBox.Text = layout.RangeLow.ToString(CultureInfo.CurrentCulture);
            this.MaxBox.Text = layout.RangeHigh.ToString(CultureInfo.CurrentCulture);
            this.AlarmLowBox.Text = layout.HasAlarms && layout.AlarmLow > layout.RangeLow
                ? layout.AlarmLow.ToString(CultureInfo.CurrentCulture) : string.Empty;
            this.AlarmHighBox.Text = layout.HasAlarms && layout.AlarmHigh < layout.RangeHigh
                ? layout.AlarmHigh.ToString(CultureInfo.CurrentCulture) : string.Empty;

            // Keep the gauge's current style on a parameter change - it is still editable below.
        }
        else
        {
            this.TitleBox.Text = chosen.Name;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => this.Close();

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_tile == null)
        {
            this.Close();
            return;
        }

        if (this.ParameterBox.SelectedItem is ParameterOption chosen && chosen.Id != _tile.ParameterId)
        {
            _tile.ParameterId = chosen.Id;
            _tile.Units = chosen.Units;
        }

        if (!string.IsNullOrWhiteSpace(this.TitleBox.Text))
        {
            _tile.Title = this.TitleBox.Text!;
        }

        if (TryParse(this.MinBox.Text, out double min))
        {
            _tile.Min = min;
        }

        if (TryParse(this.MaxBox.Text, out double max))
        {
            _tile.Max = max;
        }

        _tile.AlarmLow = TryParse(this.AlarmLowBox.Text, out double alarmLow) ? alarmLow : null;
        _tile.AlarmHigh = TryParse(this.AlarmHighBox.Text, out double alarmHigh) ? alarmHigh : null;

        _tile.Kind = this.DialRadio.IsChecked == true ? GaugeKind.Round
            : this.BarRadio.IsChecked == true ? GaugeKind.Bar
            : GaugeKind.Text;

        this.Close();
    }

    private static bool TryParse(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
}
