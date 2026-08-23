// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Prompts for a PCM's external security key (e.g. the E92's 5-byte seed/key), showing the seed
    /// the PCM returned so the user can compute the key. Returns the entered key bytes via
    /// <see cref="KeyBytes"/> when the dialog result is OK.
    /// </summary>
    public sealed class SecurityKeyDialogBox : Form
    {
        private readonly TextBox keyTextBox;
        private readonly int keyByteCount;

        /// <summary>The parsed key bytes when the result is OK; null otherwise.</summary>
        public byte[]? KeyBytes { get; private set; }

        public SecurityKeyDialogBox(PcmType pcmType, byte[] seed)
        {
            this.keyByteCount = seed.Length;

            this.Text = pcmType + " Security Key";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(372, 138);

            Label prompt = new Label { Text = "The " + pcmType + " returned this security seed:", Left = 12, Top = 14, AutoSize = true };
            Label seedLabel = new Label
            {
                Text = seed.ToHex(string.Empty),
                Left = 12,
                Top = 36,
                Width = 348,
                Font = new Font(FontFamily.GenericMonospace, 11, FontStyle.Bold),
            };
            Label enter = new Label { Text = "Enter the " + this.keyByteCount + "-byte key (hex):", Left = 12, Top = 66, AutoSize = true };
            this.keyTextBox = new TextBox
            {
                Left = 12,
                Top = 86,
                Width = 348,
                CharacterCasing = CharacterCasing.Upper,
                MaxLength = (this.keyByteCount * 2) + this.keyByteCount, // allow spaces between bytes
                Font = new Font(FontFamily.GenericMonospace, 10, FontStyle.Regular),
            };

            Button okButton = new Button { Text = "OK", Left = 204, Top = 112, Width = 75, DialogResult = DialogResult.OK };
            Button cancelButton = new Button { Text = "Cancel", Left = 285, Top = 112, Width = 75, DialogResult = DialogResult.Cancel };

            okButton.Click += (sender, e) =>
            {
                byte[]? key = Utility.TryParseHex(this.keyTextBox.Text);
                if (key == null || key.Length != this.keyByteCount)
                {
                    MessageBox.Show(
                        this,
                        "Enter exactly " + this.keyByteCount + " hex bytes (" + (this.keyByteCount * 2) + " hex digits).",
                        "Invalid key",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None; // keep the dialog open
                    return;
                }
                this.KeyBytes = key;
            };

            this.Controls.Add(prompt);
            this.Controls.Add(seedLabel);
            this.Controls.Add(enter);
            this.Controls.Add(this.keyTextBox);
            this.Controls.Add(okButton);
            this.Controls.Add(cancelButton);
            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;
        }
    }
}
