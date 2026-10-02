// SPDX-License-Identifier: GPL-3.0-only
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
        private ParameterDatabase database = null!;
        private bool suspendSelectionEvents = true;

        /// <summary>
        /// Generic PIDs this PCM said it does not have, which nothing may tick on its behalf.
        /// </summary>
        /// <remarks>
        /// The dashboard ticks and locks the rows its gauges need, and a module's own refusal has to
        /// outrank that - otherwise the default dashboard would keep re-selecting a parameter the
        /// PCM has already denied, and the row would flip back to ticked every time the locks were
        /// re-applied.
        /// </remarks>
        private readonly HashSet<uint> deniedPids = new HashSet<uint>();

        private void FillParameterGrid()
        {
            // First, empty the grid.
            this.parameterGrid.Rows.Clear();

            // Not GetExecutingAssembly().Location: that is empty in the single-exe build, which
            // would make Path.GetDirectoryName throw.
            string appDirectory = AppContext.BaseDirectory;

            this.database = new ParameterDatabase(appDirectory);

            this.database.LoadDatabase();

            // What the PCM interface can supply. The auxiliary bus has its own list on its own tab,
            // because which connection a value comes from is answered by which list you are in
            // rather than by a control on every row.
            foreach (Parameter parameter in
                this.database.ListParametersForConnection(osid, ParameterSources.PcmConnection))
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

            // Rebuilding the grid recreated every row, so the dashboard's selection and locks went
            // with them.
            this.ReapplyDashboardSelection();

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

            // Opening a profile clears every tick before re-applying its own, so the dashboard's
            // requirements have to be re-asserted afterwards.
            this.ReapplyDashboardSelection();
        }

        private void parameterGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // This ensures that checkbox changes are committed immediately.
            // By default they are on committed when focus leaves the cell.
            DataGridViewCheckBoxCell? checkBoxCell = this.parameterGrid.CurrentCell as DataGridViewCheckBoxCell;
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

        /// <summary>
        /// Untick parameters the PCM refused, so the grid shows what is actually being logged.
        /// </summary>
        /// <remarks>
        /// Done without rebuilding the profile: the logger has already been built around the columns
        /// that survived, and replacing the profile here would have the logging thread tear it down
        /// and start again. The grid is brought into line with the session, not the other way round.
        /// </remarks>
        private void UntickUnsupportedParameters(IReadOnlyList<Parameter> unsupported)
        {
            if (unsupported.Count == 0 || this.parameterGrid.Rows.Count == 0)
            {
                return;
            }

            HashSet<string> drop = new HashSet<string>(
                unsupported.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

            try
            {
                this.suspendSelectionEvents = true;

                foreach (DataGridViewRow row in this.parameterGrid.Rows)
                {
                    if (row.Cells[CellIndexParameter].Value is Parameter parameter
                        && drop.Contains(parameter.Id))
                    {
                        row.Cells[CellIndexEnable].Value = false;
                        row.Cells[CellIndexZoom].Value = false;
                    }
                }
            }
            finally
            {
                this.suspendSelectionEvents = false;
            }

            this.AddUserMessage(
                "Removed from the list: " + string.Join(", ", unsupported.Select(p => p.Name)));
        }

        /// <summary>
        /// Grey out the generic PIDs the PCM says it does not have.
        /// </summary>
        /// <remarks>
        /// Only the generic range is judged. The manufacturer's own PIDs are absent from the
        /// reported set whether or not the module has them, so switching those off would hide most
        /// of the useful parameters on the strength of a question that was never asked about them.
        ///
        /// A null set means the PCM could not say, and then nothing is disabled: an older module
        /// that does not answer is not a module with no parameters.
        /// </remarks>
        private void DisableUnsupportedPids(HashSet<uint>? supported)
        {
            if (this.parameterGrid.Rows.Count == 0)
            {
                return;
            }

            try
            {
                this.suspendSelectionEvents = true;

                int disabled = 0;
                this.deniedPids.Clear();

                foreach (DataGridViewRow row in this.parameterGrid.Rows)
                {
                    if (!(row.Cells[CellIndexParameter].Value is PidParameter pid))
                    {
                        continue;
                    }

                    bool judged = supported != null && pid.PID <= GenericPidCeiling;
                    bool missing = judged && !supported!.Contains(pid.PID);

                    row.ReadOnly = missing;
                    row.DefaultCellStyle.ForeColor = missing ? AppTheme.MutedText : AppTheme.Text;

                    if (missing)
                    {
                        row.Cells[CellIndexEnable].Value = false;
                        row.Cells[CellIndexZoom].Value = false;
                        this.deniedPids.Add(pid.PID);
                        disabled++;
                    }
                }

                if (disabled > 0)
                {
                    this.AddUserMessage($"{disabled} parameters are not supported by this PCM.");
                }
            }
            finally
            {
                this.suspendSelectionEvents = false;
            }
        }

        /// <summary>Whether this PCM has already refused to supply this parameter.</summary>
        private bool IsDeniedByPcm(Parameter parameter)
        {
            return this.deniedPids.Count > 0
                && parameter is PidParameter pid
                && this.deniedPids.Contains(pid.PID);
        }

        /// <summary>
        /// The highest PID the support masks cover. Above this are the manufacturer's own, which
        /// the masks say nothing about.
        /// </summary>
        private const uint GenericPidCeiling = 0xFF;

        private void LogProfileChanged()
        {
            if (this.suspendSelectionEvents)
            {
                return;
            }
 
            this.ResetProfile();

            if (this.ZoomVisible)
            {
                this.ClearZoomPanel();
            }

            this.CreateProfileFromGrid();

            // Different parameters means a different width of text in the values pane.
            this.RefitValuesPaneSoon();

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
                    Conversion? conversion = null;

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
                    LogColumn column = new LogColumn(parameter, conversion!, zoom);
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
                Parameter? parameter = row.Cells[CellIndexParameter].Value as Parameter;
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
