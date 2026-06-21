// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Recovery-mode prompt. When the PCM cannot be identified automatically, the user
    /// selects the PCM type directly from this list rather than hunting for a matching OSID.
    /// </summary>
    public sealed class PcmTypeSelectorDialogBox : Form
    {
        private readonly ComboBox pcmTypeComboBox;
        private readonly Button okButton;
        private readonly Button cancelButton;

        /// <summary>
        /// The PCM type the user selected, or PcmType.Undefined if the dialog was cancelled.
        /// </summary>
        public PcmType SelectedPcmType { get; private set; } = PcmType.Undefined;

        public PcmTypeSelectorDialogBox()
        {
            this.Text = "Select PCM Type";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(360, 120);

            Label prompt = new Label
            {
                Text = "The PCM could not be identified automatically." + Environment.NewLine +
                       "Select the PCM type to continue:",
                Location = new Point(12, 12),
                AutoSize = true
            };

            this.pcmTypeComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(12, 52),
                Size = new Size(336, 21)
            };

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

            if (this.pcmTypeComboBox.Items.Count > 0)
            {
                this.pcmTypeComboBox.SelectedIndex = 0;
            }

            this.okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(192, 86),
                Size = new Size(75, 25)
            };
            this.okButton.Click += this.OkButton_Click;

            this.cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(273, 86),
                Size = new Size(75, 25)
            };

            this.Controls.Add(prompt);
            this.Controls.Add(this.pcmTypeComboBox);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.cancelButton);

            this.AcceptButton = this.okButton;
            this.CancelButton = this.cancelButton;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            if (this.pcmTypeComboBox.SelectedItem == null)
            {
                this.SelectedPcmType = PcmType.Undefined;
                return;
            }

            this.SelectedPcmType = (PcmType)Enum.Parse(typeof(PcmType), this.pcmTypeComboBox.SelectedItem.ToString());
        }
    }
}
