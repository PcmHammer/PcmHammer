// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Module import, gated by the "Allow module import" setting. Lists the loaded package's image slots
    /// (main plus any slave modules) with a Replace button on each, so a module can be swapped for a file
    /// of matching size (slave) or format (main). Changes apply in place to the package;
    /// <see cref="Changed"/> reports whether any were made.
    /// </summary>
    public class ModuleImportDialog : Form
    {
        private readonly PcmPackage package;
        private readonly ILogger logger;

        /// <summary>True when at least one slot was replaced.</summary>
        public bool Changed { get; private set; }

        public ModuleImportDialog(PcmPackage package, ILogger logger)
        {
            this.package = package;
            this.logger = logger;

            this.Text = "Import Module";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(560, 340);

            Label intro = new Label
            {
                Text = "Replace a module with a file of the same size (slave) or format (main). This builds a file " +
                       "with a specific module - use only if you know what you are doing.",
                Location = new Point(12, 10),
                Size = new Size(536, 34),
            };

            Panel rowPanel = new Panel
            {
                Location = new Point(12, 48),
                Size = new Size(536, 244),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };

            Button closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Size = new Size(84, 28),
                Location = new Point(this.ClientSize.Width - 96, this.ClientSize.Height - 40),
            };

            this.Controls.Add(intro);
            this.Controls.Add(rowPanel);
            this.Controls.Add(closeButton);
            this.AcceptButton = closeButton;
            this.CancelButton = closeButton;

            this.BuildRows(rowPanel);
        }

        private void BuildRows(Panel host)
        {
            int y = 8;
            foreach (PackageController controller in this.package.Controllers)
            {
                Label header = new Label
                {
                    Text = string.Format("Controller {0} ({1})",
                        controller.Id, controller.ModuleType ?? controller.Type ?? "PCM"),
                    Font = new Font(this.Font, FontStyle.Bold),
                    Location = new Point(8, y),
                    AutoSize = true,
                };
                host.Controls.Add(header);
                y += 24;

                foreach (PackageImage image in controller.Images)
                {
                    Label detail = new Label
                    {
                        Location = new Point(24, y + 5),
                        Size = new Size(370, 30),
                        Text = Describe(image),
                    };

                    Button replace = new Button
                    {
                        Text = "Replace...",
                        Location = new Point(404, y),
                        Size = new Size(104, 26),
                    };

                    PackageController capturedController = controller;
                    PackageImage capturedImage = image;
                    Label capturedDetail = detail;
                    replace.Click += (s, e) => this.ReplaceSlot(capturedController, capturedImage, capturedDetail);

                    host.Controls.Add(detail);
                    host.Controls.Add(replace);
                    y += 40;
                }

                y += 8;
            }
        }

        /// <summary>One-line description of a slot's current content: target, size, id, and source.</summary>
        private static string Describe(PackageImage image)
        {
            string target = image.Target ?? "image";
            long size = CurrentSize(image);
            string sizeText = size > 0 ? FormatSize(size) : "size unknown";
            string source =
                image.Data != null ? "embedded" :
                SlaveLibrary.Find(image.FileName) != null ? "from library" :
                "reference, not in library";
            string id =
                image.Osid != null ? ", OSID " + image.Osid :
                image.PartNumber != null ? ", p/n " + image.PartNumber :
                string.Empty;
            return string.Format("{0}: {1}{2} ({3})", target, sizeText, id, source);
        }

        private void ReplaceSlot(PackageController controller, PackageImage image, Label detail)
        {
            bool isMain = string.Equals(image.Target, "main", StringComparison.OrdinalIgnoreCase);

            string? path = this.PickFile();
            if (path == null)
            {
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                this.Warn("Unable to open file: " + exception.Message);
                return;
            }

            bool ok = isMain
                ? this.ReplaceMain(controller, image, bytes, path)
                : this.ReplaceSlave(image, bytes, path);
            if (!ok)
            {
                return;
            }

            detail.Text = Describe(image);
            this.Changed = true;
        }

        /// <summary>
        /// Main slot: the file must be a valid image for this app (format gate) and match the controller's
        /// module type. If the controller carries slave references bound to a different main OSID, warn.
        /// </summary>
        private bool ReplaceMain(PackageController controller, PackageImage image, byte[] bytes, string path)
        {
            FileValidator validator = new FileValidator(bytes, this.logger);
            if (!validator.IdentifyAndValidate())
            {
                this.Warn("This file is corrupt or its format is unknown to PCM Hammer. It was not imported.");
                return false;
            }

            PcmType fileType = validator.GetFileType();
            uint fileOsid = validator.GetOsidFromImage();

            if (!string.IsNullOrEmpty(controller.ModuleType) &&
                !string.Equals(fileType.ToString(), controller.ModuleType, StringComparison.OrdinalIgnoreCase))
            {
                this.Warn(string.Format(
                    "This is a {0} image, but the controller is {1}. It was not imported.", fileType, controller.ModuleType));
                return false;
            }

            bool hasSlave = controller.Images.Any(
                i => i.Target != null && i.Target.StartsWith("slave", StringComparison.OrdinalIgnoreCase));
            if (hasSlave && image.Osid != null && fileOsid != 0 && image.Osid != fileOsid)
            {
                DialogResult choice = MessageBox.Show(this,
                    string.Format(
                        "The slave modules in this package were recorded for main OSID {0}, but this image is OSID {1}." + Environment.NewLine +
                        "Writing this combination may produce a non-functional module." + Environment.NewLine + Environment.NewLine +
                        "Import anyway?", image.Osid, fileOsid),
                    "Possible slave mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes)
                {
                    return false;
                }
            }

            image.Data = bytes;
            image.Osid = fileOsid != 0 ? fileOsid : (uint?)null;
            this.logger.AddUserMessage(string.Format(
                "Imported main from {0} (OSID {1}, {2}).", Path.GetFileName(path), fileOsid, new OSIDInfo(fileType).Description));
            return true;
        }

        /// <summary>
        /// Slave slot: we cannot yet validate slave content, so the gate is size - the file must match the
        /// slot's current size (from its bytes or the local-library copy). If that size is unknown, confirm.
        /// </summary>
        private bool ReplaceSlave(PackageImage image, byte[] bytes, string path)
        {
            long expected = CurrentSize(image);
            if (expected > 0 && bytes.LongLength != expected)
            {
                this.Warn(string.Format(
                    "The {0} module is {1}; this file is {2}. Import a file of the same size.",
                    image.Target, FormatSize(expected), FormatSize(bytes.LongLength)));
                return false;
            }

            if (expected == 0)
            {
                DialogResult choice = MessageBox.Show(this,
                    string.Format(
                        "The current size of \"{0}\" is unknown (it is not in the local library), so the file cannot be size-checked." + Environment.NewLine +
                        "Import anyway?", image.Target),
                    "Unverified size", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes)
                {
                    return false;
                }
            }

            image.Data = bytes;

            // The imported file becomes this slot's content; reflect its name (and part number if the name
            // is numeric, as factory slave modules are).
            string name = Path.GetFileName(path);
            image.FileName = name;
            if (uint.TryParse(Path.GetFileNameWithoutExtension(path), out uint partNumber))
            {
                image.PartNumber = partNumber;
            }

            this.logger.AddUserMessage(string.Format(
                "Imported {0} from {1} ({2}).", image.Target, name, FormatSize(bytes.LongLength)));
            return true;
        }

        /// <summary>The slot's current size in bytes: its embedded bytes, else the local-library copy, else 0.</summary>
        private static long CurrentSize(PackageImage image)
        {
            if (image.Data != null)
            {
                return image.Data.LongLength;
            }

            string? path = SlaveLibrary.Find(image.FileName);
            return path != null ? new FileInfo(path).Length : 0;
        }

        private string? PickFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.DefaultExt = ".bin";
                dialog.Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*";
                dialog.FilterIndex = 1;
                dialog.RestoreDirectory = true;
                if (!string.IsNullOrWhiteSpace(Configuration.Settings.BinDirectory))
                {
                    dialog.InitialDirectory = Configuration.Settings.BinDirectory;
                }
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private static string FormatSize(long bytes) =>
            (bytes >= 1024 && bytes % 1024 == 0) ? (bytes / 1024) + " KiB" : bytes + " bytes";

        private void Warn(string message)
        {
            this.logger.AddUserMessage(message);
            MessageBox.Show(this, message, "Import Module", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
