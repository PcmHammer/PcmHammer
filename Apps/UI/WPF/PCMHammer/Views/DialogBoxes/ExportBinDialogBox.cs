// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PCMHammer.Views.DialogBoxes
{
    /// <summary>One exportable image and the controller it belongs to.</summary>
    public class ExportBinSelection
    {
        public required PackageController Controller { get; set; }
        public required PackageImage Image { get; set; }
    }

    /// <summary>
    /// Export Bin picker (WPF): lists the controllers in the loaded document and the image(s) inside each,
    /// with a checkbox per exportable image. Defaults to the first main image. OK returns the checked
    /// images (the caller writes each to its own .bin); Cancel returns nothing. Reference images (slave
    /// modules whose bytes aren't in the local library) are listed but cannot be checked.
    /// This mirrors the WinForms ExportBinDialogBox.
    /// </summary>
    public class ExportBinDialogBox : Window
    {
        private readonly List<(CheckBox Box, ExportBinSelection Selection)> rows = new();

        public ExportBinDialogBox(PcmPackage package)
        {
            this.Title = "Export Bin";
            this.Width = 420;
            this.Height = 340;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ResizeMode = ResizeMode.NoResize;
            this.ShowInTaskbar = false;

            Grid grid = new() { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock prompt = new()
            {
                Text = "Choose the image(s) to export as raw .bin files:",
                Margin = new Thickness(0, 0, 0, 8),
            };
            Grid.SetRow(prompt, 0);
            grid.Children.Add(prompt);

            StackPanel list = new();
            CheckBox? firstMainBox = null;
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

                    CheckBox box = new()
                    {
                        Content = module + " / " + (image.Target ?? "image") + suffix,
                        IsEnabled = exportable,
                        Margin = new Thickness(0, 2, 0, 2),
                    };
                    var selection = new ExportBinSelection { Controller = controller, Image = image };
                    this.rows.Add((box, selection));
                    list.Children.Add(box);

                    if (exportable)
                    {
                        this.HasExportableImages = true;
                        if (firstMainBox == null &&
                            string.Equals(image.Target, "main", StringComparison.OrdinalIgnoreCase))
                        {
                            firstMainBox = box;
                        }
                    }
                }
            }

            if (firstMainBox != null)
            {
                firstMainBox.IsChecked = true;
            }

            ScrollViewer scroller = new()
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                Padding = new Thickness(6),
            };
            Grid.SetRow(scroller, 1);
            grid.Children.Add(scroller);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Button saveAs = new() { Content = "Save As...", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            Button cancel = new() { Content = "Cancel", Width = 90, IsCancel = true };
            saveAs.Click += (s, e) => { this.DialogResult = true; };
            buttons.Children.Add(saveAs);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            this.Content = grid;
        }

        /// <summary>True when at least one image in the package carries bytes that can be exported.</summary>
        public bool HasExportableImages { get; private set; }

        /// <summary>The checked, exportable images.</summary>
        public List<ExportBinSelection> SelectedImages
        {
            get
            {
                var result = new List<ExportBinSelection>();
                foreach (var (box, selection) in this.rows)
                {
                    if (box.IsEnabled && box.IsChecked == true)
                    {
                        result.Add(selection);
                    }
                }
                return result;
            }
        }
    }
}
