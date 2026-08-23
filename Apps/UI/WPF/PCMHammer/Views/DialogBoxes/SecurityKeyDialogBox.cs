// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCMHammer.Views.DialogBoxes
{
    /// <summary>
    /// Prompts for a PCM's external security key (e.g. the E92's 5-byte seed/key), showing the seed
    /// the PCM returned so the user can compute the key. When the dialog result is true,
    /// <see cref="KeyBytes"/> holds the entered key.
    /// </summary>
    public sealed class SecurityKeyDialogBox : Window
    {
        private readonly TextBox keyTextBox;
        private readonly int keyByteCount;

        /// <summary>The parsed key bytes when the result is true; null otherwise.</summary>
        public byte[]? KeyBytes { get; private set; }

        public SecurityKeyDialogBox(PcmType pcmType, byte[] seed)
        {
            this.keyByteCount = seed.Length;

            this.Title = pcmType + " Security Key";
            this.SizeToContent = SizeToContent.WidthAndHeight;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ShowInTaskbar = false;

            StackPanel panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock
            {
                Text = "The " + pcmType + " returned this security seed:",
                Margin = new Thickness(0, 0, 0, 4),
            });
            panel.Children.Add(new TextBlock
            {
                Text = seed.ToHex(string.Empty),
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 10),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Enter the " + this.keyByteCount + "-byte key (hex):",
                Margin = new Thickness(0, 0, 0, 4),
            });
            this.keyTextBox = new TextBox
            {
                FontFamily = new FontFamily("Consolas"),
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            panel.Children.Add(this.keyTextBox);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            Button okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            Button cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            okButton.Click += (sender, e) =>
            {
                byte[]? key = Utility.TryParseHex(this.keyTextBox.Text);
                if (key == null || key.Length != this.keyByteCount)
                {
                    MessageBox.Show(
                        this,
                        "Enter exactly " + this.keyByteCount + " hex bytes (" + (this.keyByteCount * 2) + " hex digits).",
                        "Invalid key",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                this.KeyBytes = key;
                this.DialogResult = true;
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            panel.Children.Add(buttons);

            this.Content = panel;
            this.Loaded += (sender, e) => this.keyTextBox.Focus();
        }
    }
}
