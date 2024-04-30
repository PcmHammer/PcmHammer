using PcmHacking;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    public partial class MainForm
    {
        private static readonly string CanConversionSettingsKey = "CanConversions";
        private bool initializingCanParameterGrid = false;

        private void selectCanButton_Click(object sender, EventArgs e)
        {
            string canPort = DeviceConfiguration.Settings.CanPort;
            CanForm canForm = new CanForm(this, canPort);

            switch (canForm.ShowDialog())
            {
                case DialogResult.OK:
                    if (canForm.SelectedPort == null)
                    {
                        this.canPortName = null;
                        DeviceConfiguration.Settings.CanPort = null;
                    }
                    else
                    {
                        this.canPortName = canForm.SelectedPort.PortName;
                        DeviceConfiguration.Settings.CanPort = this.canPortName;
                    }

                    DeviceConfiguration.Settings.Save();
                    this.canDeviceDescription.Text = this.canPortName;

                    // Re-create the logger, so it starts using the new port.
                    this.ResetProfile();
                    this.CreateProfileFromGrid();
                    break;

                case DialogResult.Cancel:
                    break;
            }
        }

        private void enableCanLogging_CheckedChanged(object sender, EventArgs e)
        {
            this.enableCanControls(true, true);
        }

        private void disableCanLogging_CheckedChanged(object sender, EventArgs e)
        {
            this.enableCanControls(false, true);
        }

        private void enableCanControls(bool enabled, bool reset)
        {
            this.selectCanButton.Enabled = enabled;
            this.canDeviceDescription.Enabled = enabled;
            this.canParameterGrid.Enabled = enabled;
            this.canParameterGrid.ReadOnly = !enabled;

            if (enabled)
            {
                this.canDeviceDescription.Text = this.canPortName;
            }
            else
            {
                this.canDeviceDescription.Text = string.Empty;
            }

            DeviceConfiguration.Settings.CanPort = this.canDeviceDescription.Text;
            DeviceConfiguration.Settings.Save();

            if (reset)
            {
                this.ResetProfile();
                this.CreateProfileFromGrid();
            }
        }

        private string GetSettingsKey(CanParameter parameter)
        {
            return "CanParameter_" + parameter.Name + "_Units";
        }

        private void FillCanParameterGrid()
        {
            this.initializingCanParameterGrid = true;
            this.canParameterGrid.Rows.Clear();
            foreach (CanParameter parameter in this.database.CanParameters)
            {
                DataGridViewRow row = new DataGridViewRow();
                row.CreateCells(this.canParameterGrid);
                row.Cells[0].Value = parameter;

                DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)row.Cells[1];
                cell.DisplayMember = "Units";
                cell.ValueMember = "Units";

                foreach(Conversion conversion in parameter.Conversions)
                {
                    cell.Items.Add(conversion);
                }

                Conversion selectedConversion = null;
                try
                {
                    SerializableStringDictionary dictionary = Configuration.Settings[CanConversionSettingsKey] as SerializableStringDictionary;
                    if (dictionary == null)
                    {
                        dictionary = new SerializableStringDictionary();
                        Configuration.Settings[CanConversionSettingsKey] = dictionary;
                        Configuration.Settings.Save();
                    }

                    string selectedUnits = dictionary[this.GetSettingsKey(parameter)];
                    if (selectedUnits != null)
                    {
                        selectedConversion = parameter.Conversions.Where(x => x.Units == selectedUnits).FirstOrDefault();
                    }
                }
                catch (SettingsPropertyNotFoundException)
                {
                    Configuration.Settings[CanConversionSettingsKey] = new SerializableStringDictionary();
                }
                catch (Exception ex)
                {
                    // this space intentionally left blank
                    ex.ToString();
                }

                if (selectedConversion == null)
                {
                    selectedConversion = parameter.Conversions.First();
                }

                cell.Value = selectedConversion;
                parameter.SelectedConversion = selectedConversion;
                this.canParameterGrid.Rows.Add(row);
            }
            this.initializingCanParameterGrid = false;
        }

        private void canParameterGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (this.canParameterGrid.IsCurrentCellDirty)
            {
                this.canParameterGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void canParameterGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 || this.initializingCanParameterGrid)
            {
                return;
            }

            DataGridViewRow row = this.canParameterGrid.Rows[e.RowIndex];

            CanParameter parameter = row.Cells[0].Value as CanParameter;

            DataGridViewComboBoxCell cell = row.Cells[e.ColumnIndex] as DataGridViewComboBoxCell;
            string conversionName = cell.Value as string;
            foreach (Conversion conversion in parameter.Conversions)
            {
                if (conversion.Units == conversionName)
                {
                    parameter.SelectedConversion = conversion;
                    SerializableStringDictionary dictionary = Configuration.Settings[CanConversionSettingsKey] as SerializableStringDictionary;
                    dictionary[this.GetSettingsKey(parameter)] = conversion.Units;
                    Configuration.Settings.Save();
                    this.AddDebugMessage($"Changed CAN parameter ${parameter.Name} units to ${conversion.Units}");
                    break;
                }
            }

        }


        private void canParameterGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            this.AddDebugMessage("CAN parameter grid: " + e.Exception.ToString());
            e.ThrowException = false;
        }
    }
}
