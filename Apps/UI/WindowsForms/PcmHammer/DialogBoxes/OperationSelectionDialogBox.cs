// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    public sealed class OperationSelection
    {
        public bool IsWrite { get; set; }
        public WriteType WriteType { get; set; }
        public bool UseAutoPcmType { get; set; }
        public PcmType SelectedPcmType { get; set; }
    }

    public sealed class OperationSelectionDialogBox : Form
    {
        private readonly RadioButton readRadioButton;
        private readonly RadioButton writeRadioButton;
        private readonly GroupBox writeGroup;
        private readonly RadioButton fullCloneRadioButton;
        private readonly RadioButton osCalRadioButton;
        private readonly RadioButton calibrationRadioButton;
        private readonly RadioButton parametersRadioButton;
        private readonly RadioButton testWriteRadioButton;
        private readonly ComboBox pcmTypeComboBox;
        private readonly Label detectionLabel;
        private readonly Button okButton;
        private readonly Button cancelButton;

        public OperationSelection? Selection { get; private set; }

        /// <param name="detected">
        /// The PCM found by probing the bus before the dialog opened, or null if nothing was detected.
        /// Drives which write types are offered and the default selection.
        /// </param>
        public OperationSelectionDialogBox(bool defaultIsWrite, WriteType defaultWriteType, OSIDInfo? detected)
        {
            this.Text = "Operation";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(430, 323);

            GroupBox operationGroup = new GroupBox
            {
                Text = "Operation",
                Location = new Point(12, 12),
                Size = new Size(196, 78)
            };

            this.readRadioButton = new RadioButton
            {
                Text = "Read (Full)",
                Location = new Point(12, 22),
                AutoSize = true
            };
            this.writeRadioButton = new RadioButton
            {
                Text = "Write",
                Location = new Point(12, 45),
                AutoSize = true
            };
            this.readRadioButton.CheckedChanged += this.OperationChanged;
            this.writeRadioButton.CheckedChanged += this.OperationChanged;
            operationGroup.Controls.Add(this.readRadioButton);
            operationGroup.Controls.Add(this.writeRadioButton);

            this.writeGroup = new GroupBox
            {
                Text = "Write Type",
                Location = new Point(220, 12),
                Size = new Size(196, 161)
            };

            this.fullCloneRadioButton = new RadioButton
            {
                Text = "Clone (Full Flash)",
                Location = new Point(12, 22),
                AutoSize = true
            };
            this.osCalRadioButton = new RadioButton
            {
                Text = "Operating System + Calibration",
                Location = new Point(12, 45),
                AutoSize = true
            };
            this.calibrationRadioButton = new RadioButton
            {
                Text = "Calibration",
                Location = new Point(12, 68),
                AutoSize = true
            };
            this.parametersRadioButton = new RadioButton
            {
                Text = "Parameters",
                Location = new Point(12, 91),
                AutoSize = true
            };
            this.testWriteRadioButton = new RadioButton
            {
                Text = "Test Write (writes nothing)",
                Location = new Point(12, 114),
                AutoSize = true
            };
            this.writeGroup.Controls.Add(this.fullCloneRadioButton);
            this.writeGroup.Controls.Add(this.osCalRadioButton);
            this.writeGroup.Controls.Add(this.calibrationRadioButton);
            this.writeGroup.Controls.Add(this.parametersRadioButton);
            this.writeGroup.Controls.Add(this.testWriteRadioButton);

            this.detectionLabel = new Label
            {
                Location = new Point(12, 96),
                Size = new Size(196, 58),
                AutoSize = false
            };

            GroupBox pcmTypeGroup = new GroupBox
            {
                Text = "PCM Type",
                Location = new Point(12, 183),
                Size = new Size(404, 88)
            };
            Label pcmTypeLabel = new Label
            {
                Text = "Auto uses OSID query. Manual forces selected type.",
                Location = new Point(12, 22),
                AutoSize = true
            };
            this.pcmTypeComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(12, 46),
                Size = new Size(380, 21)
            };
            this.pcmTypeComboBox.Items.Add("Auto (Query OSID)");
            foreach (PcmType type in Enum.GetValues(typeof(PcmType)).Cast<PcmType>())
            {
                if (type == PcmType.Undefined)
                {
                    continue;
                }

                OSIDInfo info = new OSIDInfo(type);
                if (!info.IsSupported)
                {
                    continue;
                }

                this.pcmTypeComboBox.Items.Add(type.ToString());
            }
            this.pcmTypeComboBox.SelectedIndex = 0;
            pcmTypeGroup.Controls.Add(pcmTypeLabel);
            pcmTypeGroup.Controls.Add(this.pcmTypeComboBox);

            this.okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(260, 281),
                Size = new Size(75, 25)
            };
            this.cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(341, 281),
                Size = new Size(75, 25)
            };

            this.AcceptButton = this.okButton;
            this.CancelButton = this.cancelButton;

            this.Controls.Add(operationGroup);
            this.Controls.Add(this.writeGroup);
            this.Controls.Add(this.detectionLabel);
            this.Controls.Add(pcmTypeGroup);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.cancelButton);

            if (defaultIsWrite)
            {
                this.writeRadioButton.Checked = true;
            }
            else
            {
                this.readRadioButton.Checked = true;
            }

            this.ApplyDetection(detected, defaultWriteType);
        }

        /// <summary>
        /// Offer only the write types the detected PCM supports, default the selection accordingly, and
        /// preselect the detected PCM type. Falls back to a fully manual choice when nothing was detected.
        /// </summary>
        private void ApplyDetection(OSIDInfo? detected, WriteType requested)
        {
            if (detected == null || !detected.IsSupported)
            {
                this.detectionLabel.Text = "Could not detect a PCM\r\nChoose the options manually";
                this.SelectWriteType(this.IsWriteTypeEnabled(requested) ? requested : WriteType.Full);
                return;
            }

            this.SelectPcmType(detected.HardwareType);

            if (!detected.IsSupportedWrite)
            {
                // Read-only PCM: there is nothing to write, so steer to Read and disable the write side.
                this.detectionLabel.Text = string.Format(
                    "Detected: {0}\r\nWriting is not supported",
                    detected.HardwareType);
                this.readRadioButton.Checked = true;
                this.writeRadioButton.Enabled = false;
                this.fullCloneRadioButton.Enabled = false;
                this.osCalRadioButton.Enabled = false;
                this.calibrationRadioButton.Enabled = false;
                this.parametersRadioButton.Enabled = false;
                this.testWriteRadioButton.Enabled = false;
                return;
            }

            // Clone is always available when writing is supported; the per-segment types need by-segment
            // support (e.g. the E38 is clone-only). A parameter write also needs a parameter block, and a
            // test write needs a kernel write path to rehearse.
            bool bySegment = detected.IsSupportedWriteBySegment;
            this.fullCloneRadioButton.Enabled = true;
            this.osCalRadioButton.Enabled = bySegment;
            this.calibrationRadioButton.Enabled = bySegment;
            this.parametersRadioButton.Enabled = detected.HasParameterBlocks;
            this.testWriteRadioButton.Enabled = detected.IsSupportedTestWrite;

            this.detectionLabel.Text = bySegment
                ? string.Format("Detected: {0}", detected.HardwareType)
                : string.Format(
                    "Detected: {0}\r\nSegment writes not supported\r\nClone only",
                    detected.HardwareType);

            // Honour the requested write type when the PCM supports it; otherwise default to calibration
            // for by-segment PCMs, or a full clone for the rest.
            WriteType preferred = this.IsWriteTypeEnabled(requested)
                ? requested
                : (bySegment ? WriteType.Calibration : WriteType.Full);
            this.SelectWriteType(preferred);
        }

        private RadioButton RadioForWriteType(WriteType writeType)
        {
            switch (writeType)
            {
                case WriteType.TestWrite:
                    return this.testWriteRadioButton;

                case WriteType.Parameters:
                    return this.parametersRadioButton;

                case WriteType.OsPlusCalibrationPlusBoot:
                    return this.osCalRadioButton;

                case WriteType.Calibration:
                    return this.calibrationRadioButton;

                default:
                    return this.fullCloneRadioButton;
            }
        }

        private bool IsWriteTypeEnabled(WriteType writeType) => this.RadioForWriteType(writeType).Enabled;

        private void SelectWriteType(WriteType writeType) => this.RadioForWriteType(writeType).Checked = true;

        private void SelectPcmType(PcmType type)
        {
            int index = this.pcmTypeComboBox.Items.IndexOf(type.ToString());
            this.pcmTypeComboBox.SelectedIndex = index > 0 ? index : 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                this.Selection = new OperationSelection
                {
                    IsWrite = this.writeRadioButton.Checked,
                    WriteType = this.GetWriteType(),
                    UseAutoPcmType = this.pcmTypeComboBox.SelectedIndex <= 0,
                    SelectedPcmType = this.GetSelectedPcmType()
                };
            }

            base.OnFormClosing(e);
        }

        private void OperationChanged(object sender, EventArgs e)
        {
            this.writeGroup.Enabled = this.writeRadioButton.Checked;
        }

        private WriteType GetWriteType()
        {
            if (this.testWriteRadioButton.Checked)
            {
                return WriteType.TestWrite;
            }

            if (this.parametersRadioButton.Checked)
            {
                return WriteType.Parameters;
            }

            if (this.osCalRadioButton.Checked)
            {
                return WriteType.OsPlusCalibrationPlusBoot;
            }

            if (this.calibrationRadioButton.Checked)
            {
                return WriteType.Calibration;
            }

            return WriteType.Full;
        }

        private PcmType GetSelectedPcmType()
        {
            if (this.pcmTypeComboBox.SelectedIndex <= 0)
            {
                return PcmType.Undefined;
            }

            return (PcmType) Enum.Parse(typeof(PcmType), this.pcmTypeComboBox.SelectedItem.ToString());
        }
    }
}
