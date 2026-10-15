// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Edits the properties of one dashboard gauge.
    /// </summary>
    /// <remarks>
    /// Deliberately the handful of fields that change how a gauge reads, not everything the format
    /// can hold. Position and size are left to the layout, and the PID binding stays on the gauge's
    /// right-click menu where it already was.
    ///
    /// Units rather than an expression: the conversion belongs to the parameter database, which is
    /// corrected and extended over time, and a per-gauge formula would freeze a copy of whatever was
    /// believed when it was written. Choosing which of a parameter's conversions to read gets the
    /// same result - volts or kPa - without a second place for the maths to be wrong.
    /// </remarks>
    internal class GaugePropertiesDialog : Form
    {
        private readonly GaugeLayout gauge;

        private readonly TextBox titleBox = new TextBox();
        private readonly ComboBox unitsBox = new ComboBox();
        private readonly ComboBox kindBox = new ComboBox();
        private readonly TextBox rangeLowBox = new TextBox();
        private readonly TextBox rangeHighBox = new TextBox();
        private readonly NumericUpDown digitsBox = new NumericUpDown();
        private readonly NumericUpDown arcBox = new NumericUpDown();
        private readonly CheckBox alarmsBox = new CheckBox();
        private readonly TextBox alarmLowBox = new TextBox();
        private readonly TextBox alarmHighBox = new TextBox();
        private readonly Button normalColorButton = new Button();
        private readonly Button alarmColorButton = new Button();

        private Color normalColor;
        private Color alarmColor;

        public GaugePropertiesDialog(GaugeLayout gauge, string parameterName, IEnumerable<string> unitOptions)
        {
            this.gauge = gauge ?? throw new ArgumentNullException(nameof(gauge));

            this.Text = "Gauge properties";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(344, 332);

            this.normalColor = FromPacked(gauge.NormalColor);
            this.alarmColor = FromPacked(gauge.AlarmColor);

            int y = 12;

            this.AddRow("Reads", y);
            Label parameter = new Label
            {
                Left = 110,
                Top = y + 3,
                Width = 220,
                Text = gauge.IsBound ? parameterName : "Not bound - use the gauge's right-click menu",
                ForeColor = gauge.IsBound ? SystemColors.ControlText : SystemColors.GrayText,
                AutoEllipsis = true,
            };
            this.Controls.Add(parameter);
            y += 28;

            this.AddRow("&Title", y);
            this.titleBox.SetBounds(110, y, 220, 22);
            this.titleBox.Text = gauge.Title;
            this.Controls.Add(this.titleBox);
            y += 28;

            this.AddRow("&Units", y);
            this.unitsBox.SetBounds(110, y, 140, 22);
            this.unitsBox.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string option in unitOptions)
            {
                this.unitsBox.Items.Add(option);
            }

            this.unitsBox.Text = gauge.Units;
            this.Controls.Add(this.unitsBox);
            y += 28;

            this.AddRow("&Style", y);
            this.kindBox.SetBounds(110, y, 140, 22);
            this.kindBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (GaugeKind kind in Enum.GetValues(typeof(GaugeKind)))
            {
                this.kindBox.Items.Add(kind);
            }

            this.kindBox.SelectedItem = gauge.Kind;
            this.kindBox.SelectedIndexChanged += (s, e) => this.UpdateEnabledState();
            this.Controls.Add(this.kindBox);
            y += 34;

            this.AddRow("&Range", y);
            this.rangeLowBox.SetBounds(110, y, 90, 22);
            this.rangeLowBox.Text = Format(gauge.RangeLow);
            this.Controls.Add(this.rangeLowBox);
            this.Controls.Add(new Label { Left = 206, Top = y + 3, Width = 14, Text = "to" });
            this.rangeHighBox.SetBounds(224, y, 90, 22);
            this.rangeHighBox.Text = Format(gauge.RangeHigh);
            this.Controls.Add(this.rangeHighBox);
            y += 28;

            this.AddRow("&Decimals", y);
            this.digitsBox.SetBounds(110, y, 50, 22);
            this.digitsBox.Minimum = 0;
            this.digitsBox.Maximum = 4;
            this.digitsBox.Value = Math.Max(0, Math.Min(4, gauge.Digits));
            this.Controls.Add(this.digitsBox);

            this.Controls.Add(new Label { Left = 176, Top = y + 3, Width = 40, Text = "Sweep" });
            this.arcBox.SetBounds(224, y, 60, 22);
            this.arcBox.Minimum = 30;
            this.arcBox.Maximum = 350;
            this.arcBox.Increment = 10;
            this.arcBox.Value = Math.Max(30, Math.Min(350, gauge.ArcDegrees));
            this.Controls.Add(this.arcBox);
            this.Controls.Add(new Label { Left = 288, Top = y + 3, Width = 20, Text = "deg" });
            y += 34;

            this.alarmsBox.SetBounds(12, y, 200, 22);
            this.alarmsBox.Text = "&Alarm outside this range";
            this.alarmsBox.Checked = gauge.HasAlarms;
            this.alarmsBox.CheckedChanged += (s, e) => this.UpdateEnabledState();
            this.Controls.Add(this.alarmsBox);
            y += 26;

            this.AddRow("Alarm range", y);
            this.alarmLowBox.SetBounds(110, y, 90, 22);
            this.alarmLowBox.Text = Format(gauge.AlarmLow);
            this.Controls.Add(this.alarmLowBox);
            this.Controls.Add(new Label { Left = 206, Top = y + 3, Width = 14, Text = "to" });
            this.alarmHighBox.SetBounds(224, y, 90, 22);
            this.alarmHighBox.Text = Format(gauge.AlarmHigh);
            this.Controls.Add(this.alarmHighBox);
            y += 34;

            this.AddRow("Colours", y);
            this.normalColorButton.SetBounds(110, y, 90, 24);
            this.normalColorButton.Text = "Normal";
            this.normalColorButton.Click += (s, e) => this.PickColor(ref this.normalColor, this.normalColorButton);
            this.Controls.Add(this.normalColorButton);

            this.alarmColorButton.SetBounds(224, y, 90, 24);
            this.alarmColorButton.Text = "Alarm";
            this.alarmColorButton.Click += (s, e) => this.PickColor(ref this.alarmColor, this.alarmColorButton);
            this.Controls.Add(this.alarmColorButton);

            this.ApplySwatch(this.normalColorButton, this.normalColor);
            this.ApplySwatch(this.alarmColorButton, this.alarmColor);
            y += 42;

            Button save = new Button { Text = "&Save", DialogResult = DialogResult.OK };
            save.SetBounds(166, y, 75, 26);
            save.Click += this.Save_Click;

            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(249, y, 75, 26);

            this.Controls.Add(save);
            this.Controls.Add(cancel);
            this.AcceptButton = save;
            this.CancelButton = cancel;

            this.UpdateEnabledState();
        }

        private void AddRow(string text, int top)
        {
            this.Controls.Add(new Label { Left = 12, Top = top + 3, Width = 95, Text = text });
        }

        private void UpdateEnabledState()
        {
            bool alarms = this.alarmsBox.Checked;
            this.alarmLowBox.Enabled = alarms;
            this.alarmHighBox.Enabled = alarms;
            this.alarmColorButton.Enabled = alarms;

            // Only a round gauge has a sweep to set.
            this.arcBox.Enabled = (this.kindBox.SelectedItem as GaugeKind?) == GaugeKind.Round;
        }

        private void PickColor(ref Color current, Button button)
        {
            using (ColorDialog dialog = new ColorDialog { Color = current, FullOpen = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    current = dialog.Color;
                    this.ApplySwatch(button, current);
                }
            }
        }

        private void ApplySwatch(Button button, Color color)
        {
            button.BackColor = color;

            // Keep the caption readable whatever colour was chosen.
            int brightness = (color.R * 299) + (color.G * 587) + (color.B * 114);
            button.ForeColor = brightness > 128000 ? Color.Black : Color.White;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
        }

        private void Save_Click(object? sender, EventArgs e)
        {
            if (!TryParse(this.rangeLowBox, "range low", out double rangeLow)
                || !TryParse(this.rangeHighBox, "range high", out double rangeHigh))
            {
                this.DialogResult = DialogResult.None;
                return;
            }

            if (rangeHigh <= rangeLow)
            {
                MessageBox.Show(
                    this,
                    "The high end of the range has to be above the low end.",
                    "Gauge properties",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            double alarmLow = this.gauge.AlarmLow;
            double alarmHigh = this.gauge.AlarmHigh;
            if (this.alarmsBox.Checked
                && (!TryParse(this.alarmLowBox, "alarm low", out alarmLow)
                    || !TryParse(this.alarmHighBox, "alarm high", out alarmHigh)))
            {
                this.DialogResult = DialogResult.None;
                return;
            }

            this.gauge.Title = this.titleBox.Text;
            this.gauge.Units = this.unitsBox.Text.Trim();
            this.gauge.Kind = (GaugeKind)(this.kindBox.SelectedItem ?? GaugeKind.Round);
            this.gauge.RangeLow = rangeLow;
            this.gauge.RangeHigh = rangeHigh;
            this.gauge.Digits = (int)this.digitsBox.Value;
            this.gauge.ArcDegrees = (int)this.arcBox.Value;
            this.gauge.HasAlarms = this.alarmsBox.Checked;
            this.gauge.AlarmLow = alarmLow;
            this.gauge.AlarmHigh = alarmHigh;
            this.gauge.NormalColor = ToPacked(this.normalColor);
            this.gauge.AlarmColor = ToPacked(this.alarmColor);
        }

        private bool TryParse(TextBox box, string what, out double value)
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            MessageBox.Show(
                this,
                $"\"{box.Text}\" is not a number, so the {what} cannot be set.",
                "Gauge properties",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            box.Focus();
            box.SelectAll();
            return false;
        }

        private static string Format(double value) =>
            value.ToString("0.####", CultureInfo.CurrentCulture);

        private static Color FromPacked(int packed) =>
            Color.FromArgb((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);

        private static int ToPacked(Color color) =>
            (color.R << 16) | (color.G << 8) | color.B;
    }
}
