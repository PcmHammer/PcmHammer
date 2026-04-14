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
        public string Vin { get; set; }

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
            if (!this.IsLegal())
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
        private void vinBox_TextChanged(object sender, EventArgs e)
        {
            this.okButton.Enabled = this.IsLegal();
        }

        /// <summary>
        /// Validate the VIN.
        /// </summary>
        private bool IsLegal()
        {

            if (this.vinBox.Text.Length != 17)
            {
                this.prompt.Text = $"The VIN must be 17 characters long.\nThis is {this.vinBox.Text.Length} characters.";
                return false;
            }

            this.vinBox.Text = this.vinBox.Text.ToUpper();

            int invalidCharacterIndex = -1;
            char requiredCheckDigit = 'X';
            if (VinValidator.IsValid(this.vinBox.Text, out invalidCharacterIndex, out requiredCheckDigit))
            {
                this.prompt.Text = "The VIN is valid. Good!";
                return true;
            }
            else
            {
                if (invalidCharacterIndex >= 0)
                {
                    char invalidCharacter = this.vinBox.Text[invalidCharacterIndex];
                    this.prompt.Text = $"The \"{invalidCharacter}\" at position {invalidCharacterIndex + 1} is not a letter or number.";
                    return false;
                }

                if (requiredCheckDigit != 'X')
                {
                    this.prompt.Text = $"The VIN check digit on position 9 is incorrect.\nCorrect check digit is: {requiredCheckDigit}";
                    return false;
                }

                this.prompt.Text = "The VIN is invalid.";
                return false;
            }
        }
    }
}
