// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>One exportable image and the controller it belongs to.</summary>
    public class ExportBinSelection
    {
        public PackageController Controller { get; set; }
        public PackageImage Image { get; set; }
    }

    /// <summary>
    /// Export Bin picker: lists the controllers in the loaded document and the image(s) inside each, with
    /// a checkbox per exportable image. Defaults to the first main image. "Save As" returns the checked
    /// images (the caller writes each to its own .bin); "Cancel" returns nothing. Reference images (slave
    /// modules with no bytes) are listed but cannot be checked - there is nothing to export.
    /// </summary>
    public class ExportBinDialogBox : Form
    {
        private readonly CheckedListBox listBox;

        private sealed class Row
        {
            public ExportBinSelection Selection;
            public bool Exportable;
            public string Label = string.Empty;
            public override string ToString() => this.Label;
        }

        public ExportBinDialogBox(PcmPackage package)
        {
            this.Text = "Export Bin";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ClientSize = new Size(380, 300);

            Label prompt = new Label
            {
                Text = "Choose the image(s) to export as raw .bin files:",
                Location = new Point(12, 10),
                AutoSize = true,
            };

            this.listBox = new CheckedListBox
            {
                Location = new Point(12, 32),
                Size = new Size(356, 216),
                CheckOnClick = true,
                IntegralHeight = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };

            Button saveAsButton = new Button
            {
                Text = "Save As...",
                DialogResult = DialogResult.OK,
                Location = new Point(202, 262),
                Size = new Size(76, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };

            Button cancelDialogButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(288, 262),
                Size = new Size(80, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };

            this.Controls.Add(prompt);
            this.Controls.Add(this.listBox);
            this.Controls.Add(saveAsButton);
            this.Controls.Add(cancelDialogButton);
            this.AcceptButton = saveAsButton;
            this.CancelButton = cancelDialogButton;

            int firstMainIndex = -1;
            foreach (PackageController controller in package.Controllers)
            {
                string module = controller.ModuleType ?? controller.Type ?? ("Controller " + controller.Id);
                foreach (PackageImage image in controller.Images)
                {
                    // An embedded image exports its own bytes; a reference exports the local library copy,
                    // when that file is present.
                    bool embedded = image.Data != null;
                    bool inLibrary = !embedded && SlaveLibrary.Find(image.FileName) != null;
                    bool exportable = embedded || inLibrary;
                    string suffix =
                        embedded ? string.Empty :
                        inLibrary ? "  (from local library)" :
                        "  (by reference - not in local library)";
                    Row row = new Row
                    {
                        Exportable = exportable,
                        Label = module + " / " + (image.Target ?? "image") + suffix,
                        Selection = new ExportBinSelection { Controller = controller, Image = image },
                    };
                    int index = this.listBox.Items.Add(row);
                    if (exportable && firstMainIndex < 0 &&
                        string.Equals(image.Target, "main", StringComparison.OrdinalIgnoreCase))
                    {
                        firstMainIndex = index;
                    }
                    if (exportable)
                    {
                        this.HasExportableImages = true;
                    }
                }
            }

            if (firstMainIndex >= 0)
            {
                this.listBox.SetItemChecked(firstMainIndex, true);
            }

            // Keep reference (non-exportable) rows from ever being checked.
            this.listBox.ItemCheck += (s, e) =>
            {
                if (e.NewValue == CheckState.Checked &&
                    this.listBox.Items[e.Index] is Row row && !row.Exportable)
                {
                    e.NewValue = CheckState.Unchecked;
                }
            };
        }

        /// <summary>True when at least one image in the package carries bytes that can be exported.</summary>
        public bool HasExportableImages { get; private set; }

        /// <summary>The checked, exportable images.</summary>
        public List<ExportBinSelection> SelectedImages
        {
            get
            {
                var result = new List<ExportBinSelection>();
                foreach (object checkedItem in this.listBox.CheckedItems)
                {
                    if (checkedItem is Row row && row.Exportable)
                    {
                        result.Add(row.Selection);
                    }
                }
                return result;
            }
        }
    }
}
