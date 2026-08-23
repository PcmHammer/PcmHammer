// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// Read-only, append-only log view that stays responsive at any size. The full log lives in
    /// memory; only the visible lines are painted, so it scrolls a huge log without slowing down.
    /// Producers append from any thread via a lock-free queue (no UI work); a UI timer flushes them
    /// in batches. Painted directly (not a ListView) so line pitch equals the font height - a themed
    /// ListView forces ~4px of extra padding per row that can't be removed.
    /// </summary>
    public class LogListView : Control
    {
        // The shared line store (queue + committed list + cap). The WPF panes use the same type.
        private readonly LogLineBuffer buffer = new LogLineBuffer();

        // Committed lines, read-only. Paint, selection and search index this exactly as before; only
        // Flush and ClearLog mutate, and they go through the buffer.
        private IReadOnlyList<string> lines => this.buffer.Lines;

        private readonly Timer flushTimer;
        private readonly VScrollBar vbar;
        private readonly HScrollBar hbar;

        private int lineHeight;
        private int charWidth;
        private int maxLineChars;
        private IntPtr fontHandle;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetTabbedTextExtent(IntPtr hdc, string text, int count, int tabCount, int[] tabStops);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        // Selection is by whole line (anchor..caret); -1 means no selection.
        private int selAnchor = -1;
        private int selCaret = -1;
        private bool selecting;

        // Ctrl+F search: matching line indices, the current one, and their highlight brushes.
        private readonly List<int> matchLines = new List<int>();
        private readonly HashSet<int> matchSet = new HashSet<int>();
        private int currentMatch = -1;
        private string searchTerm = string.Empty;
        private readonly Brush matchBrush = new SolidBrush(Color.FromArgb(255, 255, 150));
        private readonly Brush currentMatchBrush = new SolidBrush(Color.FromArgb(255, 200, 0));

        // ExpandTabs keeps tab-separated columns (e.g. the checksum validation tables) aligned. The log
        // font is monospace, so tab stops land on consistent character columns. NoPrefix stops '&' being
        // read as a mnemonic; NoPadding/SingleLine keep the text tight, and the tab-aware
        // GetTabbedTextExtent below measures the same way.
        private const TextFormatFlags TextFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.ExpandTabs;

        public LogListView()
        {
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
                true);
            this.BackColor = SystemColors.Control;
            this.ForeColor = SystemColors.WindowText;
            this.TabStop = true;

            this.lineHeight = Math.Max(1, this.Font.Height);
            this.charWidth = Math.Max(1, TextRenderer.MeasureText("0123456789", this.Font).Width / 10);
            this.UpdateFontHandle();

            this.vbar = new VScrollBar();
            this.hbar = new HScrollBar();
            this.vbar.ValueChanged += (s, e) => this.Invalidate();
            this.hbar.ValueChanged += (s, e) => this.Invalidate();
            this.Controls.Add(this.vbar);
            this.Controls.Add(this.hbar);

            this.flushTimer = new Timer { Interval = 100 };
            this.flushTimer.Tick += (s, e) => this.Flush();
            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            {
                this.flushTimer.Start();
            }
        }

        /// <summary>
        /// Cap on committed lines; oldest are dropped past this. 0 (the default) means unlimited.
        /// Used by the bus monitor to bound a high-rate feed.
        /// </summary>
        public int MaxLines
        {
            get => this.buffer.MaxLines;
            set => this.buffer.MaxLines = value;
        }

        /// <summary>Queue a line for display. Thread-safe; performs no UI work.</summary>
        public void AppendLine(string text)
        {
            this.buffer.Append(text);
        }

        /// <summary>The whole log as one newline-separated string. UI thread.</summary>
        public string GetAllText() => this.buffer.Snapshot();

        /// <summary>True when there is nothing to save. UI thread.</summary>
        public bool IsEmpty
        {
            get
            {
                this.buffer.Drain();
                return this.buffer.Count == 0;
            }
        }

        /// <summary>Discard the whole log. UI thread.</summary>
        public void ClearLog()
        {
            this.buffer.Clear();
            this.maxLineChars = 0;
            this.selAnchor = this.selCaret = -1;
            this.UpdateScrollBars();
            this.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.flushTimer.Stop();
                this.flushTimer.Dispose();
                this.matchBrush.Dispose();
                this.currentMatchBrush.Dispose();
            }

            if (this.fontHandle != IntPtr.Zero)
            {
                DeleteObject(this.fontHandle);
                this.fontHandle = IntPtr.Zero;
            }

            base.Dispose(disposing);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            this.lineHeight = Math.Max(1, this.Font.Height);
            this.charWidth = Math.Max(1, TextRenderer.MeasureText("0123456789", this.Font).Width / 10);
            this.UpdateFontHandle();
            this.UpdateScrollBars();
            this.Invalidate();
        }

        private void UpdateFontHandle()
        {
            if (this.fontHandle != IntPtr.Zero)
            {
                DeleteObject(this.fontHandle);
            }

            this.fontHandle = this.Font.ToHfont();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.UpdateScrollBars();
            this.Invalidate();
        }

        // Move queued lines into the committed list (the buffer also applies MaxLines), resize the
        // scrollbars, and keep the newest line in view only when the user is already at the bottom (so
        // manual scrolling is not yanked down).
        private void Flush()
        {
            if (!this.buffer.HasPending)
            {
                return;
            }

            bool atBottom = this.IsScrolledToBottom();

            LogLineBuffer.DrainResult result = this.buffer.Drain();
            if (!result.Changed)
            {
                return;
            }

            // Track the widest line for the horizontal scrollbar. Only the lines just added can widen
            // it; as before, a trim never shrinks it back.
            for (int i = Math.Max(0, this.lines.Count - result.AddedCount); i < this.lines.Count; i++)
            {
                if (this.lines[i].Length > this.maxLineChars)
                {
                    this.maxLineChars = this.lines[i].Length;
                }
            }

            this.UpdateScrollBars();

            if (atBottom)
            {
                this.vbar.Value = this.MaxTopLine();
            }

            this.Invalidate();
        }

        private int TextAreaWidth => Math.Max(0, this.Width - this.vbar.Width);

        private int TextAreaHeight => Math.Max(0, this.Height - (this.hbar.Visible ? this.hbar.Height : 0));

        private int VisibleLines => Math.Max(1, this.TextAreaHeight / this.lineHeight);

        // Highest first-visible line that still fills the view (the scroll-to-bottom position).
        private int MaxTopLine() => Math.Max(0, this.lines.Count - this.VisibleLines);

        private bool IsScrolledToBottom() => this.vbar.Value >= this.MaxTopLine();

        private void UpdateScrollBars()
        {
            int vw = this.vbar.Width;
            int hh = this.hbar.Height;

            // Only show the horizontal bar when a line is wider than the view; the vertical bar stays.
            int contentWidth = (this.maxLineChars + 1) * this.charWidth;
            bool hNeeded = contentWidth > this.Width - vw;
            this.hbar.Visible = hNeeded;

            int textHeight = this.Height - (hNeeded ? hh : 0);
            this.vbar.Bounds = new Rectangle(this.Width - vw, 0, vw, Math.Max(0, textHeight));
            this.hbar.Bounds = new Rectangle(0, this.Height - hh, Math.Max(0, this.Width - vw), hh);

            this.vbar.LargeChange = Math.Max(1, textHeight / this.lineHeight);
            this.vbar.SmallChange = 1;
            this.vbar.Maximum = Math.Max(0, this.lines.Count - 1);
            this.ClampVValue();

            this.hbar.LargeChange = Math.Max(1, this.Width - vw);
            this.hbar.SmallChange = this.charWidth;
            this.hbar.Maximum = Math.Max(0, contentWidth);
            this.ClampHValue();
        }

        private void ClampVValue()
        {
            int max = Math.Max(0, this.vbar.Maximum - this.vbar.LargeChange + 1);
            if (this.vbar.Value > max) { this.vbar.Value = max; }
        }

        private void ClampHValue()
        {
            int max = Math.Max(0, this.hbar.Maximum - this.hbar.LargeChange + 1);
            if (this.hbar.Value > max) { this.hbar.Value = max; }
        }

        private void SetVValue(int value)
        {
            int max = Math.Max(0, this.vbar.Maximum - this.vbar.LargeChange + 1);
            this.vbar.Value = Math.Min(Math.Max(0, value), max);
        }

        private void SetHValue(int value)
        {
            int max = Math.Max(0, this.hbar.Maximum - this.hbar.LargeChange + 1);
            this.hbar.Value = Math.Min(Math.Max(0, value), max);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(this.BackColor);

            int first = this.vbar.Value;
            int xOffset = this.hbar.Value;
            int width = this.TextAreaWidth;
            int bottom = this.TextAreaHeight;
            int selLo = Math.Min(this.selAnchor, this.selCaret);
            int selHi = Math.Max(this.selAnchor, this.selCaret);

            int y = 0;
            for (int i = first; i < this.lines.Count && y < bottom; i++, y += this.lineHeight)
            {
                bool selected = this.selAnchor >= 0 && i >= selLo && i <= selHi;
                if (selected)
                {
                    e.Graphics.FillRectangle(SystemBrushes.Highlight, new Rectangle(0, y, width, this.lineHeight));
                }
                else if (this.matchSet.Contains(i))
                {
                    this.PaintMatchHighlights(e.Graphics, i, y, xOffset);
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    this.lines[i],
                    this.Font,
                    new Point(-xOffset, y),
                    selected ? SystemColors.HighlightText : this.ForeColor,
                    TextFlags);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int linesPerNotch = SystemInformation.MouseWheelScrollLines;
            if (linesPerNotch <= 0) { linesPerNotch = 3; }
            this.SetVValue(this.vbar.Value - (e.Delta / 120) * linesPerNotch);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.Focus();
            if (e.Button == MouseButtons.Left)
            {
                int line = this.LineAt(e.Y);
                this.selAnchor = this.selCaret = line;
                this.selecting = line >= 0;
                this.Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (this.selecting)
            {
                this.selCaret = this.LineAt(e.Y);
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            this.selecting = false;
        }

        // Map a Y pixel to a line index, clamped to the populated range.
        private int LineAt(int y)
        {
            if (this.lines.Count == 0)
            {
                return -1;
            }

            int line = this.vbar.Value + Math.Max(0, y) / this.lineHeight;
            return Math.Min(line, this.lines.Count - 1);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Control && e.KeyCode == Keys.C)
            {
                this.CopySelectionToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.A)
            {
                if (this.lines.Count > 0)
                {
                    this.selAnchor = 0;
                    this.selCaret = this.lines.Count - 1;
                    this.Invalidate();
                }
                e.Handled = true;
                return;
            }

            // Shift + navigation extends the line selection instead of just scrolling.
            if (e.Shift)
            {
                int basis = this.selCaret >= 0 ? this.selCaret : this.vbar.Value;
                int target;
                switch (e.KeyCode)
                {
                    case Keys.Up: target = basis - 1; break;
                    case Keys.Down: target = basis + 1; break;
                    case Keys.PageUp: target = basis - this.VisibleLines; break;
                    case Keys.PageDown: target = basis + this.VisibleLines; break;
                    case Keys.Home: target = 0; break;
                    case Keys.End: target = this.lines.Count - 1; break;
                    default: return;
                }

                this.SelectCaretTo(target);
                e.Handled = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Up: this.SetVValue(this.vbar.Value - 1); break;
                case Keys.Down: this.SetVValue(this.vbar.Value + 1); break;
                case Keys.PageUp: this.SetVValue(this.vbar.Value - this.VisibleLines); break;
                case Keys.PageDown: this.SetVValue(this.vbar.Value + this.VisibleLines); break;
                case Keys.Home: this.SetVValue(0); break;
                case Keys.End: this.SetVValue(this.MaxTopLine()); break;
                default: return;
            }

            e.Handled = true;
        }

        // Extend the line selection to 'line', anchoring at the current caret if there isn't one yet,
        // and scroll just enough to keep the moving end on screen.
        private void SelectCaretTo(int line)
        {
            if (this.lines.Count == 0)
            {
                return;
            }

            if (this.selAnchor < 0)
            {
                this.selAnchor = this.selCaret >= 0 ? this.selCaret : this.vbar.Value;
            }

            this.selCaret = Math.Min(Math.Max(0, line), this.lines.Count - 1);
            this.EnsureLineVisible(this.selCaret);
            this.Invalidate();
        }

        private void EnsureLineVisible(int line)
        {
            if (line < this.vbar.Value)
            {
                this.SetVValue(line);
            }
            else if (line >= this.vbar.Value + this.VisibleLines)
            {
                this.SetVValue(line - this.VisibleLines + 1);
            }
        }

        /// <summary>Number of matching lines from the last <see cref="FindAll"/>.</summary>
        public int MatchCount => this.matchLines.Count;

        /// <summary>1-based index of the current match, or 0 when none is selected.</summary>
        public int CurrentMatchNumber => this.currentMatch < 0 ? 0 : this.currentMatch + 1;

        /// <summary>Highlight every line containing <paramref name="term"/> (case-insensitive). UI thread.</summary>
        public int FindAll(string term)
        {
            this.Flush();
            this.matchLines.Clear();
            this.matchSet.Clear();
            this.currentMatch = -1;
            this.searchTerm = term ?? string.Empty;

            if (!string.IsNullOrEmpty(term))
            {
                for (int i = 0; i < this.lines.Count; i++)
                {
                    if (this.lines[i].IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        this.matchLines.Add(i);
                        this.matchSet.Add(i);
                    }
                }
            }

            this.Invalidate();
            return this.matchLines.Count;
        }

        /// <summary>Step to the next (+1) or previous (-1) match, centring it in the view. Wraps around.</summary>
        public void MoveToMatch(int direction)
        {
            int count = this.matchLines.Count;
            if (count == 0)
            {
                return;
            }

            if (this.currentMatch < 0)
            {
                this.currentMatch = direction >= 0 ? 0 : count - 1;
            }
            else
            {
                this.currentMatch = ((this.currentMatch + direction) % count + count) % count;
            }

            int line = this.matchLines[this.currentMatch];
            this.SetVValue(line - this.VisibleLines / 2);

            // Centre the match horizontally too, so a hit in a long line isn't scrolled off-screen.
            int idx = this.searchTerm.Length > 0 ? this.lines[line].IndexOf(this.searchTerm, StringComparison.OrdinalIgnoreCase) : -1;
            if (idx >= 0)
            {
                using (Graphics g = this.CreateGraphics())
                {
                    int matchCentre = (this.MeasureWidth(g, this.lines[line], idx) + this.MeasureWidth(g, this.lines[line], idx + this.searchTerm.Length)) / 2;
                    this.SetHValue(matchCentre - this.TextAreaWidth / 2);
                }
            }

            this.Invalidate();
        }

        // Paint a highlight behind each occurrence of the search term in this line (current match in a
        // stronger colour), rather than filling the whole row.
        private void PaintMatchHighlights(Graphics g, int lineIndex, int y, int xOffset)
        {
            if (this.searchTerm.Length == 0)
            {
                return;
            }

            string line = this.lines[lineIndex];
            Brush brush = this.currentMatch >= 0 && this.matchLines[this.currentMatch] == lineIndex
                ? this.currentMatchBrush
                : this.matchBrush;

            int from = 0;
            while (true)
            {
                int idx = line.IndexOf(this.searchTerm, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    break;
                }

                int x1 = this.MeasureWidth(g, line, idx);
                int x2 = this.MeasureWidth(g, line, idx + this.searchTerm.Length);
                g.FillRectangle(brush, new Rectangle(-xOffset + x1, y, x2 - x1, this.lineHeight));
                from = idx + this.searchTerm.Length;
            }
        }

        // Pixel advance of the first 'count' characters via GDI, with tabs expanded the same way the
        // text is drawn, so highlight positions line up exactly with TextRenderer.DrawText
        // (TextRenderer.MeasureText adds a constant padding that would shift the highlight right).
        private int MeasureWidth(Graphics g, string line, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            IntPtr hdc = g.GetHdc();
            try
            {
                IntPtr previous = SelectObject(hdc, this.fontHandle);
                int extent = GetTabbedTextExtent(hdc, line.Substring(0, count), count, 0, null);
                SelectObject(hdc, previous);
                return extent & 0xFFFF;
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        /// <summary>Clear search highlighting. UI thread.</summary>
        public void ClearSearch()
        {
            this.matchLines.Clear();
            this.matchSet.Clear();
            this.currentMatch = -1;
            this.searchTerm = string.Empty;
            this.Invalidate();
        }

        private void CopySelectionToClipboard()
        {
            if (this.selAnchor < 0)
            {
                return;
            }

            int lo = Math.Min(this.selAnchor, this.selCaret);
            int hi = Math.Max(this.selAnchor, this.selCaret);

            StringBuilder builder = new StringBuilder();
            for (int i = lo; i <= hi && i < this.lines.Count; i++)
            {
                builder.AppendLine(this.lines[i]);
            }

            if (builder.Length > 0)
            {
                // Clipboard.SetText throws ExternalException when another process is holding the
                // clipboard open (clipboard managers, RDP/remote sessions, some IDEs). SetDataObject
                // retries for us; swallow a final failure so copying text can never crash the app.
                try
                {
                    Clipboard.SetDataObject(builder.ToString(), copy: true, retryTimes: 10, retryDelay: 50);
                }
                catch (ExternalException)
                {
                }
            }
        }
    }
}
