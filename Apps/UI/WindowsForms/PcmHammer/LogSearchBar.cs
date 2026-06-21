// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Small find bar shown in the top-right of the window for Ctrl+F. Holds the search box, a match
    /// counter, previous/next buttons and a close button; the owner wires the events to the log.
    /// </summary>
    public class LogSearchBar : Panel
    {
        private readonly TextBox input;
        private readonly Label status;
        private readonly Button previous;
        private readonly Button next;
        private readonly Button close;

        public event Action<string> QueryChanged;
        public event Action FindNext;
        public event Action FindPrevious;
        public event Action CloseRequested;

        public LogSearchBar()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = SystemColors.Window;
            this.Size = new Size(280, 26);

            this.input = new TextBox { BorderStyle = BorderStyle.None, Location = new Point(4, 5), Width = 150 };
            this.status = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(158, 0), Size = new Size(48, 24) };
            this.previous = MakeButton("▲", 208);
            this.next = MakeButton("▼", 230);
            this.close = MakeButton("✕", 252);

            this.Controls.Add(this.input);
            this.Controls.Add(this.status);
            this.Controls.Add(this.previous);
            this.Controls.Add(this.next);
            this.Controls.Add(this.close);

            this.input.TextChanged += (s, e) => this.QueryChanged?.Invoke(this.input.Text);
            this.input.KeyDown += this.OnInputKeyDown;
            this.previous.Click += (s, e) => this.FindPrevious?.Invoke();
            this.next.Click += (s, e) => this.FindNext?.Invoke();
            this.close.Click += (s, e) => this.CloseRequested?.Invoke();
        }

        public string Query => this.input.Text;

        public void SetStatus(int current, int total)
        {
            this.status.Text = this.input.Text.Length == 0 ? string.Empty : current + "/" + total;
        }

        public void FocusInput()
        {
            this.input.Focus();
            this.input.SelectAll();
        }

        private Button MakeButton(string text, int x)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, 1),
                Size = new Size(22, 22),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Margin = Padding.Empty,
            };
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (e.Shift) { this.FindPrevious?.Invoke(); } else { this.FindNext?.Invoke(); }
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.CloseRequested?.Invoke();
                e.Handled = e.SuppressKeyPress = true;
            }
        }
    }
}
