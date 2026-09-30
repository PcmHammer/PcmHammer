// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking.DialogBoxes
{
    /// <summary>
    /// Prompt the user to enter a valid VIN.
    /// </summary>
    public partial class VinForm : Form
    {
        /// <summary>
        /// This will be copied into the text box when the dialog box appears.
        /// When the dialog closes, if the user provided a valid VIN it will
        /// be returned via this property. If they didn't, this will be null.
        /// </summary>
        public string? Vin { get; set; }

        /// <summary>
        /// The latest assessment of what is in the text box. Holds the message, whether the VIN can be
        /// written, and the check digit the "Fix VIN" button would apply.
        /// </summary>
        private VinAssessment assessment = VinAssessment.Of(string.Empty);

        /// <summary>
        /// Constructor.
        /// </summary>
        public VinForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load event handler.
        /// </summary>
        private void VinForm_Load(object sender, EventArgs e)
        {
            this.vinBox.Text = this.Vin;
            this.vinBox_TextChanged(null, null);
        }

        /// <summary>
        /// OK button click handler.
        /// </summary>
        private void okButton_Click(object sender, EventArgs e)
        {
            // Only the 17-character rule blocks OK. A VIN that fails the standard's check digit is
            // still accepted, because CAN PCMs are routinely found with one (see VinAssessment).
            if (!this.assessment.CanWrite)
            {
                return;
            }

            this.Vin = this.vinBox.Text;

            this.DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// Cancel button click handler.
        /// </summary>
        private void cancelButton_Click(object sender, EventArgs e)
        {
            this.Vin = null;

            this.DialogResult = DialogResult.Cancel;
        }

        /// <summary>
        /// Validate the new VIN every time it changes.
        /// </summary>
        private void vinBox_TextChanged(object? sender, EventArgs? e)
        {
            if (this.vinBox.Text.Length == 17)
            {
                this.vinBox.Text = this.vinBox.Text.ToUpper();
            }

            this.assessment = VinAssessment.Of(this.vinBox.Text);
            this.prompt.Text = this.assessment.Message;
            this.okButton.Enabled = this.assessment.CanWrite;

            // "Fix VIN" is offered only when the check digit is the thing that does not match, and only
            // as a convenience - the VIN is writable either way.
            this.fixVinButton.Enabled = this.assessment.SuggestedCheckDigit != 'X';
        }

        /// <summary>
        /// Replace the check digit (position 9) with the value the standard calls for.
        /// </summary>
        private void fixVinButton_Click(object sender, EventArgs e)
        {
            // Re-validates via TextChanged, which then disables this button.
            this.vinBox.Text = this.assessment.ApplySuggestedCheckDigit(this.vinBox.Text);
        }
    }
}
