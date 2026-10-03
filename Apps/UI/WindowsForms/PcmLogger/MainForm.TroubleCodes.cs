// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// The Trouble Codes tab: read what the module has logged, and clear it.
    /// </summary>
    /// <remarks>
    /// Built in code, like the Dash and Monitors tabs, so MainForm.Designer stays as generated.
    ///
    /// Reading is why clearing can now ask first. Clearing has been in the app for years with no way
    /// to see what was about to go, which is a poor thing to offer: codes are the evidence, and
    /// erasing evidence unseen is how a fault gets diagnosed twice.
    /// </remarks>
    partial class MainForm
    {
        private readonly TabPage troubleCodesTab =
            new TabPage { Text = "Trouble Codes", UseVisualStyleBackColor = true };

        private ListView? troubleCodeList;
        private Button? readCodesButton;
        private Button? clearCodesButton;
        private Label? troubleCodeStatus;

        private DiagnosticCodeDefinitions codeDefinitions = new DiagnosticCodeDefinitions();

        /// <summary>What the last read found, so Clear can say what it is about to erase.</summary>
        private IReadOnlyList<DiagnosticCode> lastReadCodes = Array.Empty<DiagnosticCode>();

        private void BuildTroubleCodesTab()
        {
            this.codeDefinitions = DiagnosticCodeDefinitions.Load(AppContext.BaseDirectory, this);

            Panel top = new Panel { Dock = DockStyle.Top, Height = 34 };

            this.readCodesButton = new Button { Text = "&Read Codes", Left = 3, Top = 4, Width = 100 };
            this.readCodesButton.Click += this.ReadCodes_Click;

            this.clearCodesButton = new Button
            {
                Text = "&Clear Codes", Left = 109, Top = 4, Width = 100, Enabled = false,
            };
            this.clearCodesButton.Click += this.ClearCodes_Click;

            this.troubleCodeStatus = new Label
            {
                Left = 217, Top = 9, AutoSize = true, Text = "Connect, then read.",
            };

            top.Controls.Add(this.readCodesButton);
            top.Controls.Add(this.clearCodesButton);
            top.Controls.Add(this.troubleCodeStatus);

            this.troubleCodeList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
            };

            this.troubleCodeList.Columns.Add("Code", 80);
            this.troubleCodeList.Columns.Add("Status", 160);
            this.troubleCodeList.Columns.Add("Description", 520);

            this.troubleCodesTab.Controls.Add(this.troubleCodeList);
            this.troubleCodesTab.Controls.Add(top);
        }

        private async void ReadCodes_Click(object? sender, EventArgs e)
        {
            Vehicle? vehicle = this.Vehicle;
            if (vehicle == null)
            {
                this.SetTroubleCodeStatus("No interface is connected.");
                return;
            }

            try
            {
                this.SetCodeButtonsEnabled(false);
                this.SetTroubleCodeStatus("Reading...");

                // The logging thread owns the bus. Borrowing it is quieter than making the user
                // disconnect: logging resumes afterwards, recording and all, a few rows short.
                if (!await this.BorrowBus())
                {
                    this.SetTroubleCodeStatus("The logger did not release the interface.");
                    return;
                }

                try
                {
                    IReadOnlyList<DiagnosticCode> codes =
                        await vehicle.ReadTroubleCodes(this.codeDefinitions, CancellationToken.None);

                    this.lastReadCodes = codes;
                    this.ShowTroubleCodes(codes);
                }
                finally
                {
                    this.ReturnBus();
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage("Unable to read trouble codes: " + exception.Message);
                this.AddDebugMessage(exception.ToString());
                this.SetTroubleCodeStatus("Unable to read trouble codes.");
            }
            finally
            {
                this.SetCodeButtonsEnabled(true);
            }
        }

        private void ShowTroubleCodes(IReadOnlyList<DiagnosticCode> codes)
        {
            if (this.troubleCodeList == null)
            {
                return;
            }

            this.troubleCodeList.Items.Clear();

            foreach (DiagnosticCode code in codes)
            {
                ListViewItem item = new ListViewItem(code.Name);

                item.SubItems.Add(code.Status.HasValue
                    ? Protocol.DescribeTroubleCodeStatus(code.Status.Value)
                    : code.Kind.ToString());

                // Named rather than guessed at when the definitions have nothing for it.
                item.SubItems.Add(code.Description ?? "No description available");
                this.troubleCodeList.Items.Add(item);
            }

            this.SetTroubleCodeStatus(
                codes.Count == 0 ? "No trouble codes." : $"{codes.Count} trouble codes.");

            if (this.clearCodesButton != null)
            {
                // Offered whenever anything was found, including a list of nothing but permanent
                // codes. Those are cleared by the module rather than by a tool, so the request is
                // expected to achieve nothing - but letting it run and reporting what came back
                // beats a greyed-out button that cannot be told apart from a broken one.
                this.clearCodesButton.Enabled = codes.Count > 0;
            }
        }

        private async void ClearCodes_Click(object? sender, EventArgs e)
        {
            Vehicle? vehicle = this.Vehicle;
            if (vehicle == null)
            {
                this.SetTroubleCodeStatus("No interface is connected.");
                return;
            }

            string list = string.Join(Environment.NewLine, Names(this.lastReadCodes));

            // Said plainly rather than hidden behind a disabled button: a permanent code stays until
            // the module's own monitor passes, so a clear that leaves one is working correctly.
            string note = HasPermanentCodes(this.lastReadCodes)
                ? Environment.NewLine + Environment.NewLine +
                  "Permanent codes are cleared by the module, not by a tool, so those will remain."
                : string.Empty;

            if (MessageBox.Show(
                    this,
                    "Clear these codes?" + Environment.NewLine + Environment.NewLine + list + note,
                    "Clear Trouble Codes",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            try
            {
                this.SetCodeButtonsEnabled(false);

                if (!await this.BorrowBus())
                {
                    this.SetTroubleCodeStatus("The logger did not release the interface.");
                    return;
                }

                try
                {
                    // The list on screen is left alone: what actually went is a question only
                    // another read can answer, and emptying it here would assert more than is known.
                    if (await vehicle.ClearTroubleCodes(CancellationToken.None))
                    {
                        this.SetTroubleCodeStatus("Clear request sent. Read again to see what remains.");
                    }
                    else
                    {
                        this.SetTroubleCodeStatus("The module did not accept the clear request.");
                    }
                }
                finally
                {
                    this.ReturnBus();
                }
            }
            catch (Exception exception)
            {
                this.AddUserMessage("Unable to clear trouble codes: " + exception.Message);
                this.AddDebugMessage(exception.ToString());
                this.SetTroubleCodeStatus("Unable to clear trouble codes.");
            }
            finally
            {
                this.SetCodeButtonsEnabled(true);
            }
        }

        private static IEnumerable<string> Names(IReadOnlyList<DiagnosticCode> codes)
        {
            foreach (DiagnosticCode code in codes)
            {
                yield return code.Kind == DiagnosticCodeKind.Permanent
                    ? code.ToString() + "  (permanent)"
                    : code.ToString();
            }
        }

        private static bool HasPermanentCodes(IReadOnlyList<DiagnosticCode> codes)
        {
            foreach (DiagnosticCode code in codes)
            {
                if (code.Kind == DiagnosticCodeKind.Permanent)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetTroubleCodeStatus(string text)
        {
            if (this.troubleCodeStatus != null)
            {
                this.troubleCodeStatus.Text = text;
            }
        }

        private void SetCodeButtonsEnabled(bool enabled)
        {
            if (this.readCodesButton != null)
            {
                this.readCodesButton.Enabled = enabled;
            }

            if (this.clearCodesButton != null)
            {
                this.clearCodesButton.Enabled = enabled && this.lastReadCodes.Count > 0;
            }
        }
    }
}
