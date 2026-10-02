// SPDX-License-Identifier: GPL-3.0-only
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
        private bool initializingBusParameterGrid = false;

        /// <summary>
        /// Whether second-bus logging has actually been asked for.
        /// </summary>
        /// <remarks>
        /// The View toggle is the truth, and it is deliberately off by default. A remembered port is
        /// not consent to open it: the second-bus adapter and the main interface can be the same
        /// hardware - an OBDX Pro on J2534 has a COM port underneath - and opening it behind the
        /// user's back fails with an access denial that looks like the app cannot decide which
        /// interface it is using.
        /// </remarks>
        private bool CanLoggingEnabled => this.AuxiliaryBusEnabled;

        /// <summary>The interface currently assigned to the auxiliary bus.</summary>
        private DeviceSelection AuxiliarySelection => new DeviceSelection(
            Configuration.Settings.AuxiliaryDeviceCategory,
            Configuration.Settings.AuxiliaryJ2534DeviceType,
            Configuration.Settings.AuxiliarySerialPort,
            Configuration.Settings.AuxiliarySerialPortDeviceType);

        /// <summary>
        /// Choose the interface for the auxiliary bus, using the same picker as the PCM interface.
        /// </summary>
        /// <remarks>
        /// The standard picker rather than the old serial-only dialog, so the auxiliary bus is an
        /// ordinary interface choice - including "None", which is how a slot is emptied.
        ///
        /// A clash is resolved by eviction rather than refusal: picking the interface the PCM is
        /// using releases it from the PCM instead of telling the user to go and undo that first.
        /// Refusing created a dead end when swapping the two slots over, and eviction cannot leave
        /// both slots holding one interface, which is the state that actually breaks things.
        /// </remarks>
        private void selectCanButton_Click(object sender, EventArgs e)
        {
            using (DevicePicker picker = new DevicePicker(this))
            {
                picker.InitialSelection = this.AuxiliarySelection;
                picker.Note = "This interface watches a second bus; the PCM uses the one on the Configuration tab.";

                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                DeviceSelection chosen = new DeviceSelection(
                    picker.DeviceCategory,
                    picker.J2534DeviceType,
                    picker.SerialPort,
                    picker.SerialPortDeviceType);

                if (chosen.IsSelected && !chosen.IsUsable)
                {
                    this.AddUserMessage("That interface is not fully specified, so the auxiliary bus is unchanged.");
                    return;
                }

                if (chosen.ConflictsWith(DeviceFactory.SelectionFromSettings()))
                {
                    this.ReleasePcmInterface(chosen);
                }

                Configuration.Settings.AuxiliaryDeviceCategory = chosen.Category;
                Configuration.Settings.AuxiliaryJ2534DeviceType = chosen.J2534DeviceType;
                Configuration.Settings.AuxiliarySerialPort = chosen.SerialPort;
                Configuration.Settings.AuxiliarySerialPortDeviceType = chosen.SerialPortDeviceType;
                Configuration.Save(this);

                this.canDeviceDescription.Text = chosen.Describe();

                // Re-create the logger, so it starts using the new interface.
                this.ResetProfile();
                this.CreateProfileFromGrid();
            }
        }

        /// <summary>
        /// Hand the PCM's interface over to the auxiliary bus, because the user has just chosen it
        /// there and one interface cannot serve both.
        /// </summary>
        private void ReleasePcmInterface(DeviceSelection taken)
        {
            DeviceConfiguration.Settings.DeviceCategory = DeviceConfiguration.Constants.DeviceCategoryNone;
            DeviceConfiguration.Settings.J2534DeviceType = string.Empty;
            DeviceConfiguration.Settings.SerialPort = string.Empty;
            DeviceConfiguration.Settings.SerialPortDeviceType = string.Empty;
            DeviceConfiguration.Save(this);

            this.AddUserMessage(
                taken.Describe() + " was in use for the PCM, so it has been released. "
                + "Choose a PCM interface on the Configuration tab.");
        }

        // Superseded by the View toggle, which both reveals this tab and enables the bus. The
        // designer still wires these, so they stay as no-ops rather than being torn out of generated
        // code; the radio buttons themselves are hidden in EnableCanControls.
        private void enableCanLogging_CheckedChanged(object sender, EventArgs e)
        {
        }

        private void disableCanLogging_CheckedChanged(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Set up the second-bus tab. The tab is only on screen when the bus is enabled, so its
        /// controls are simply usable - there is no disabled state to represent any more.
        /// </summary>
        private void EnableCanControls(bool enabled, bool reset)
        {
            // The View toggle took over from these, and a tab that is hidden when off has no use for
            // an on/off control of its own.
            this.enableCanLogging.Visible = false;
            this.disableCanLogging.Visible = false;

            this.selectCanButton.Enabled = true;
            this.canDeviceDescription.Enabled = true;
            this.canParameterGrid.Enabled = true;
            this.canParameterGrid.ReadOnly = false;

            this.canDeviceDescription.Text = this.AuxiliarySelection.Describe();

            if (reset)
            {
                this.ResetProfile();
                this.CreateProfileFromGrid();
            }
        }

        private string GetSettingsKey(BusParameter parameter)
        {
            // Still "CanParameter_": this is a stored settings key, and renaming it would orphan
            // every unit choice anyone has already made.
            return "CanParameter_" + parameter.Name + "_Units";
        }

        private const int CellIndexBusConstraint = 2;

        private static string DescribeBus(BusProtocol bus) =>
            bus == BusProtocol.Can500k ? "CAN" : "VPW";

        private void FillBusParameterGrid()
        {
            this.initializingBusParameterGrid = true;
            this.canParameterGrid.Rows.Clear();
            // What the auxiliary interface can supply, by source - the counterpart of the PCM list
            // on the Parameters tab.
            foreach (BusParameter parameter in
                this.database.ListParametersForConnection(this.osid, ParameterSources.AuxiliaryConnection)
                    .OfType<BusParameter>())
            {
                DataGridViewRow row = new DataGridViewRow();
                row.CreateCells(this.canParameterGrid);
                row.Cells[CellIndexEnable].Value = parameter;

                DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)row.Cells[1];
                cell.DisplayMember = "Units";
                cell.ValueMember = "Units";

                foreach(Conversion conversion in parameter.Conversions)
                {
                    cell.Items.Add(conversion);
                }

                Conversion? selectedConversion = null;
                try
                {
                    SerializableStringDictionary? dictionary = Configuration.Settings[CanConversionSettingsKey] as SerializableStringDictionary;
                    if (dictionary == null)
                    {
                        dictionary = new SerializableStringDictionary();
                        Configuration.Settings[CanConversionSettingsKey] = dictionary;
                        Configuration.Save(this);
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

                // Stated, not enforced: which bus this interface is on is not known until it
                // connects, so the list says what a parameter needs and the mismatch is reported
                // when it turns out to matter.
                row.Cells[CellIndexBusConstraint].Value = parameter.RequiredBus.HasValue
                    ? DescribeBus(parameter.RequiredBus.Value) + " only"
                    : string.Empty;

                this.canParameterGrid.Rows.Add(row);
            }
            this.initializingBusParameterGrid = false;
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
            if (e.RowIndex == -1 || this.initializingBusParameterGrid)
            {
                return;
            }

            DataGridViewRow row = this.canParameterGrid.Rows[e.RowIndex];

            BusParameter? parameter = row.Cells[CellIndexEnable].Value as BusParameter;

            DataGridViewComboBoxCell? cell = row.Cells[e.ColumnIndex] as DataGridViewComboBoxCell;
            if (cell == null || parameter == null) return;
            string? conversionName = cell.Value as string;
            foreach (Conversion conversion in parameter.Conversions)
            {
                if (conversion.Units == conversionName)
                {
                    parameter.SelectedConversion = conversion;
                    SerializableStringDictionary? dictionary = Configuration.Settings[CanConversionSettingsKey] as SerializableStringDictionary;
                    if (dictionary != null) { dictionary[this.GetSettingsKey(parameter)] = conversion.Units; }
                    Configuration.Save(this);
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
