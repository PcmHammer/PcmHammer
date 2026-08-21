// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.Win32;
using PcmHacking;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PCMHammer.Views.DialogBoxes
{
    /// <summary>
    /// Module import (WPF), gated by the "Allow module import" setting. Lists the loaded package's image
    /// slots (main plus any slave modules) with a Replace button on each, so a module can be swapped for a
    /// file of matching size (slave) or format (main). Changes apply in place to the package;
    /// <see cref="Changed"/> reports whether any were made. Mirrors the WinForms ModuleImportDialog.
    /// </summary>
    public class ModuleImportDialog : Window
    {
        private readonly PcmPackage package;
        private readonly ILogger logger;

        /// <summary>True when at least one slot was replaced.</summary>
        public bool Changed { get; private set; }

        public ModuleImportDialog(PcmPackage package, ILogger logger)
        {
            this.package = package;
            this.logger = logger;

            this.Title = "Import Module";
            this.Width = 600;
            this.Height = 380;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ResizeMode = ResizeMode.NoResize;
            this.ShowInTaskbar = false;

            Grid grid = new() { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock intro = new()
            {
                Text = "Replace a module with a file of the same size (slave) or format (main). This builds a file " +
                       "with a specific module - use only if you know what you are doing.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            Grid.SetRow(intro, 0);
            grid.Children.Add(intro);

            StackPanel rowHost = new();
            this.BuildRows(rowHost);

            ScrollViewer scroller = new()
            {
                Content = rowHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                Padding = new Thickness(6),
            };
            Grid.SetRow(scroller, 1);
            grid.Children.Add(scroller);

            Button close = new()
            {
                Content = "Close",
                Width = 90,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
                IsDefault = true,
                IsCancel = true,
            };
            Grid.SetRow(close, 2);
            grid.Children.Add(close);

            this.Content = grid;
        }

        private void BuildRows(Panel host)
        {
            foreach (PackageController controller in this.package.Controllers)
            {
                TextBlock header = new()
                {
                    Text = string.Format("Controller {0} ({1})",
                        controller.Id, controller.ModuleType ?? controller.Type ?? "PCM"),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 6, 0, 4),
                };
                host.Children.Add(header);

                foreach (PackageImage image in controller.Images)
                {
                    Grid row = new() { Margin = new Thickness(12, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    TextBlock detail = new()
                    {
                        Text = Describe(image),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    Grid.SetColumn(detail, 0);
                    row.Children.Add(detail);

                    Button replace = new()
                    {
                        Content = "Replace...",
                        Width = 100,
                        Margin = new Thickness(8, 0, 0, 0),
                    };
                    PackageController capturedController = controller;
                    PackageImage capturedImage = image;
                    TextBlock capturedDetail = detail;
                    replace.Click += (s, e) => this.ReplaceSlot(capturedController, capturedImage, capturedDetail);
                    Grid.SetColumn(replace, 1);
                    row.Children.Add(replace);

                    host.Children.Add(row);
                }
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

        private void ReplaceSlot(PackageController controller, PackageImage image, TextBlock detail)
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
            FileValidator validator = new(bytes, this.logger);
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
                MessageBoxResult choice = MessageBox.Show(this,
                    string.Format(
                        "The slave modules in this package were recorded for main OSID {0}, but this image is OSID {1}." + Environment.NewLine +
                        "Writing this combination may produce a non-functional module." + Environment.NewLine + Environment.NewLine +
                        "Import anyway?", image.Osid, fileOsid),
                    "Possible slave mismatch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.Yes)
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
                MessageBoxResult choice = MessageBox.Show(this,
                    string.Format(
                        "The current size of \"{0}\" is unknown (it is not in the local library), so the file cannot be size-checked." + Environment.NewLine +
                        "Import anyway?", image.Target),
                    "Unverified size", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.Yes)
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
            OpenFileDialog dialog = new()
            {
                DefaultExt = ".bin",
                Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true,
            };
            if (!string.IsNullOrWhiteSpace(PCMHammer.Properties.Settings.Default.BinDirectory))
            {
                dialog.InitialDirectory = PCMHammer.Properties.Settings.Default.BinDirectory;
            }
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private static string FormatSize(long bytes) =>
            (bytes >= 1024 && bytes % 1024 == 0) ? (bytes / 1024) + " KiB" : bytes + " bytes";

        private void Warn(string message)
        {
            this.logger.AddUserMessage(message);
            MessageBox.Show(this, message, "Import Module", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
