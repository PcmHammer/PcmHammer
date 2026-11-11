using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PcmHacking;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Views;

/// <summary>
/// A small modal dialog to edit one gauge's title, range, alarms and style. Writes straight back to
/// the GaugeTile on OK, which the dashboard rebinds to live.
/// </summary>
public partial class GaugeEditDialog : Window
{
    private readonly GaugeTile? _tile;

    public GaugeEditDialog()
    {
        InitializeComponent();
    }

    public GaugeEditDialog(GaugeTile tile)
        : this()
    {
        _tile = tile;

        this.TitleBox.Text = tile.Title;
        this.MinBox.Text = tile.Min.ToString(CultureInfo.CurrentCulture);
        this.MaxBox.Text = tile.Max.ToString(CultureInfo.CurrentCulture);
        this.AlarmLowBox.Text = tile.AlarmLow?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        this.AlarmHighBox.Text = tile.AlarmHigh?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        this.StyleBox.SelectedIndex = tile.Kind == GaugeKind.Round ? 0 : tile.Kind == GaugeKind.Bar ? 2 : 1;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => this.Close();

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_tile == null)
        {
            this.Close();
            return;
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

        _tile.Kind = this.StyleBox.SelectedIndex switch
        {
            0 => GaugeKind.Round,
            2 => GaugeKind.Bar,
            _ => GaugeKind.Text,
        };

        this.Close();
    }

    private static bool TryParse(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
}
