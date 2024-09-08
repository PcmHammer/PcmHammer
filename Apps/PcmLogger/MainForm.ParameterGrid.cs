using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    public partial class MainForm
    {
        const int CellIndexEnable = 0;
        const int CellIndexZoom = 1;
        const int CellIndexParameter = 2;
        const int CellIndexUnits = 3;

        //private Dictionary<string, DataGridViewRow> parameterIdsToRows;
        private ParameterDatabase database;
        private bool suspendSelectionEvents = true;

        private void FillParameterGrid()
        {
            // First, empty the grid.
            this.parameterGrid.Rows.Clear();

            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string appDirectory = Path.GetDirectoryName(appPath);

            this.database = new ParameterDatabase(appDirectory);

            this.database.LoadDatabase();

            foreach (Parameter parameter in this.database.ListParametersBySupportedOs(osid))
            {
                DataGridViewRow row = new DataGridViewRow();

                row.CreateCells(this.parameterGrid);

                row.Cells[CellIndexEnable].Value = false; // enabled
                row.Cells[CellIndexZoom].Value = false; // zoom
                row.Cells[CellIndexParameter].Value = parameter;

                DataGridViewComboBoxCell unitsCell = (DataGridViewComboBoxCell)row.Cells[CellIndexUnits];

                unitsCell.DisplayMember = "Units";
                unitsCell.ValueMember = "Units";

                foreach (Conversion conversion in parameter.Conversions)
                {
                    unitsCell.Items.Add(conversion);
                }

                unitsCell.Value = parameter.Conversions.First();

                this.parameterGrid.Rows.Add(row);
            }

            this.suspendSelectionEvents = false;

            if (!this.parameterSearch.Focused)
            {
                this.ShowSearchPrompt();
            }
        }

        private void UpdateGridFromProfile()
        {
            try
            {
                this.suspendSelectionEvents = true;

                foreach (DataGridViewRow row in this.parameterGrid.Rows)
                {
                    row.Cells[CellIndexEnable].Value = false;
                    row.Cells[CellIndexZoom].Value = false;
                }

                foreach (LogColumn column in this.currentProfile.Columns)
                {
                    DataGridViewRow row = this.parameterGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(
                        r => r.Cells[CellIndexParameter].Value == column.Parameter);

                    if (row != null)
                    {
                        row.Cells[CellIndexEnable].Value = true;
                        if (column.Zoom)
                        {
                            row.Cells[CellIndexZoom].Value = true;
                        }

                        DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)(row.Cells[CellIndexUnits]);
                        Conversion profileConversion = column.Conversion;
                        string profileUnits = column.Conversion.Units;

                        foreach (Conversion conversion in cell.Items)
                        {
                            if ((conversion == profileConversion) || (conversion.Units == profileUnits))
                            {
                                cell.Value = conversion;
                            }
                        }
                    }
                }
            }
            finally
            {
                this.suspendSelectionEvents = false;
            }
        }

        private void parameterGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // This ensures that checkbox changes are committed immediately.
            // By default they are on committed when focus leaves the cell.
            DataGridViewCheckBoxCell checkBoxCell = this.parameterGrid.CurrentCell as DataGridViewCheckBoxCell;
            if ((checkBoxCell != null) && checkBoxCell.IsInEditMode && this.parameterGrid.IsCurrentCellDirty)
            {
                this.parameterGrid.EndEdit();
            }
        }

        private void parameterGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // Prevent the user from checking the Zoom box if the parameter is not
            // enabled. I had hoped to disable the Zoom boxes until the corresponding
            // parameter is enabled, but DataGridView doesn't support that.
            if (this.parameterGrid.CurrentCell.ColumnIndex == CellIndexZoom)
            {
                int rowIndex = this.parameterGrid.CurrentCell.RowIndex;
                DataGridViewCell enabledCell = this.parameterGrid.Rows[rowIndex].Cells[CellIndexEnable];
                if ((bool)enabledCell.Value == false)
                {
                    this.parameterGrid.CancelEdit();
                    return;
                }
            }

            // This ensures that checkbox changes are committed immediately.
            // By default they are on committed when focus leaves the cell.
            if (this.parameterGrid.IsCurrentCellDirty)
            {
                this.parameterGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void parameterGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            this.LogProfileChanged();
        }

        private void LogProfileChanged()
        { 
            if (this.suspendSelectionEvents)
            {
                return;
            }
 
            this.ResetProfile();

            this.ClearZoomPanel();

            this.CreateProfileFromGrid();

            this.SetDirtyFlag(true);
        }

        private void CreateProfileFromGrid()
        {
            this.ResetProfile();

            foreach (DataGridViewRow row in this.parameterGrid.Rows)
            {
                if ((bool)row.Cells[CellIndexEnable].Value == true)
                {
                    Parameter parameter = (Parameter)row.Cells[CellIndexParameter].Value;
                    Conversion conversion = null;

                    DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)(row.Cells[CellIndexUnits]);
                    foreach (Conversion candidate in cell.Items)
                    {
                        // The fact that we have to do both kinds of comparisons here really
                        // seems like a bug in the DataGridViewComboBoxCell code:
                        if ((candidate.Units == cell.Value as string) ||
                            (candidate == cell.Value as Conversion))
                        {
                            conversion = candidate;
                            break;
                        }
                    }

                    bool zoom = (bool)row.Cells[CellIndexZoom].Value;
                    LogColumn column = new LogColumn(parameter, conversion, zoom);
                    this.currentProfile.AddColumn(column);
                }
            }
        }

        #region Parameter search
        private bool showSearchPrompt = true;

        private void ShowSearchPrompt()
        {
            this.parameterSearch.Text = "";
            parameterSearch_Leave(this, new EventArgs());
        }

        private void parameterSearch_Enter(object sender, EventArgs e)
        {
            if (this.showSearchPrompt)
            {
                this.parameterSearch.Text = "";
                this.showSearchPrompt = false;
                return;
            }
        }

        private void parameterSearch_Leave(object sender, EventArgs e)
        {
            if (this.parameterSearch.Text.Length == 0)
            {
                this.showSearchPrompt = true;
                this.parameterSearch.Text = "Search...";
                return;
            }
        }

        private void parameterSearch_TextChanged(object sender, EventArgs e)
        {
            if (this.showSearchPrompt)
            {
                return;
            }

            foreach (DataGridViewRow row in this.parameterGrid.Rows)
            {
                Parameter parameter = row.Cells[CellIndexParameter].Value as Parameter;
                if (parameter == null)
                {
                    continue;
                }
                
                if (parameter.Name.IndexOf(this.parameterSearch.Text, StringComparison.CurrentCultureIgnoreCase) == -1)
                {
                    row.Visible = false;
                }
                else
                {
                    row.Visible = true;
                }
            }
        }
        #endregion
    }
}
