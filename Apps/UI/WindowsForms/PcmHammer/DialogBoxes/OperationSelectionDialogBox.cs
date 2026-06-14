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
        private readonly RadioButton parametersRadioButton;
        private readonly ComboBox pcmTypeComboBox;
        private readonly Button okButton;
        private readonly Button cancelButton;

        public OperationSelection? Selection { get; private set; }

        public OperationSelectionDialogBox(bool defaultIsWrite, WriteType defaultWriteType)
        {
            this.Text = "Operation";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(430, 280);

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
                Size = new Size(196, 112)
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
            this.parametersRadioButton = new RadioButton
            {
                Text = "Parameters",
                Location = new Point(12, 68),
                AutoSize = true
            };
            this.writeGroup.Controls.Add(this.fullCloneRadioButton);
            this.writeGroup.Controls.Add(this.osCalRadioButton);
            this.writeGroup.Controls.Add(this.parametersRadioButton);

            GroupBox pcmTypeGroup = new GroupBox
            {
                Text = "PCM Type",
                Location = new Point(12, 102),
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

                OSIDInfo info = new(type);
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
                Location = new Point(260, 230),
                Size = new Size(75, 25)
            };
            this.cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(341, 230),
                Size = new Size(75, 25)
            };

            this.AcceptButton = this.okButton;
            this.CancelButton = this.cancelButton;

            this.Controls.Add(operationGroup);
            this.Controls.Add(this.writeGroup);
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

            switch (defaultWriteType)
            {
                case WriteType.Parameters:
                    this.parametersRadioButton.Checked = true;
                    break;

                case WriteType.OsPlusCalibrationPlusBoot:
                    this.osCalRadioButton.Checked = true;
                    break;

                default:
                    this.fullCloneRadioButton.Checked = true;
                    break;
            }
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
            if (this.parametersRadioButton.Checked)
            {
                return WriteType.Parameters;
            }

            if (this.osCalRadioButton.Checked)
            {
                return WriteType.OsPlusCalibrationPlusBoot;
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
