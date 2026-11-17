// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PcmHacking
{
    /// <summary>
    /// A parameter picked for a gauge, with the scale its units usually read on where one is known.
    /// </summary>
    internal class ParameterChoice
    {
        public ParameterChoice(BindingCandidate candidate, bool hasRange, double rangeLow, double rangeHigh)
        {
            this.Candidate = candidate;
            this.HasRange = hasRange;
            this.RangeLow = rangeLow;
            this.RangeHigh = rangeHigh;
        }

        public BindingCandidate Candidate { get; }

        public bool HasRange { get; }

        public double RangeLow { get; }

        public double RangeHigh { get; }
    }

    /// <summary>
    /// Pick one parameter from the list the connected PCM supports, narrowing it by typing.
    /// </summary>
    /// <remarks>
    /// The list is long enough that scrolling for a name is not realistic, so the box filters as
    /// the user types. Every word typed has to appear in the name, in any order, so "speed eng"
    /// finds "Engine Speed".
    /// </remarks>
    internal class ParameterPickerDialog : Form
    {
        private readonly IReadOnlyList<BindingCandidate> candidates;
        private readonly HashSet<string> unavailable;
        private readonly TextBox searchBox = new TextBox();
        private readonly ListBox list = new ListBox();
        private readonly Button ok = new Button();

        /// <param name="candidates">Every parameter that can be offered.</param>
        /// <param name="currentId">The gauge's present parameter, shown selected to start with.</param>
        /// <param name="unavailable">
        /// Parameters another gauge already shows. They are listed but cannot be chosen: the logger
        /// polls one conversion per parameter, so a second gauge in different units would change the
        /// first one's reading.
        /// </param>
        public ParameterPickerDialog(
            IReadOnlyList<BindingCandidate> candidates,
            string? currentId,
            IEnumerable<string>? unavailable = null)
        {
            this.candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            this.unavailable = new HashSet<string>(unavailable ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            this.Text = "Choose a parameter";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(360, 380);

            this.Controls.Add(new Label { Left = 12, Top = 15, Width = 50, Text = "&Search" });

            this.searchBox.SetBounds(66, 12, 282, 22);
            this.searchBox.TextChanged += (s, e) => this.Refilter();
            this.Controls.Add(this.searchBox);

            this.list.SetBounds(12, 42, 336, 290);
            this.list.IntegralHeight = false;
            this.list.DrawMode = DrawMode.OwnerDrawFixed;
            this.list.DrawItem += this.List_DrawItem;
            this.list.SelectedIndexChanged += (s, e) => this.ok.Enabled = this.CanChooseSelected;
            this.list.DoubleClick += (s, e) =>
            {
                if (this.CanChooseSelected)
                {
                    this.DialogResult = DialogResult.OK;
                }
            };
            this.Controls.Add(this.list);

            this.ok.Text = "&Choose";
            this.ok.DialogResult = DialogResult.OK;
            this.ok.SetBounds(192, 342, 75, 26);
            this.Controls.Add(this.ok);

            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(273, 342, 75, 26);
            this.Controls.Add(cancel);

            this.AcceptButton = this.ok;
            this.CancelButton = cancel;

            this.Refilter();

            // Start on the current binding, so changing it shows where it came from.
            if (currentId != null)
            {
                foreach (object item in this.list.Items)
                {
                    if (string.Equals(((Entry)item).Candidate.Id, currentId, StringComparison.OrdinalIgnoreCase))
                    {
                        this.list.SelectedItem = item;
                        break;
                    }
                }
            }

            this.ok.Enabled = this.CanChooseSelected;
        }

        /// <summary>The parameter chosen, once the dialog has returned OK.</summary>
        public BindingCandidate? Selected => this.CanChooseSelected ? (this.list.SelectedItem as Entry)?.Candidate : null;

        private bool CanChooseSelected => this.list.SelectedItem is Entry entry && entry.Available;

        /// <summary>Taken parameters are drawn in the muted colour, whether or not they are highlighted.</summary>
        private void List_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }

            Entry entry = (Entry)this.list.Items[e.Index];
            e.DrawBackground();
            Color color = entry.Available ? e.ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(e.Graphics, entry.ToString(), e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.searchBox.Focus();
        }

        private void Refilter()
        {
            string[] words = this.searchBox.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            this.list.BeginUpdate();
            this.list.Items.Clear();
            foreach (BindingCandidate candidate in this.candidates.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (words.All(w => candidate.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    this.list.Items.Add(new Entry(candidate, !this.unavailable.Contains(candidate.Id)));
                }
            }

            // Start on something that can be chosen, so Enter does what it appears to.
            for (int i = 0; i < this.list.Items.Count; i++)
            {
                if (((Entry)this.list.Items[i]).Available)
                {
                    this.list.SelectedIndex = i;
                    break;
                }
            }

            this.list.EndUpdate();
        }

        private class Entry
        {
            public Entry(BindingCandidate candidate, bool available)
            {
                this.Candidate = candidate;
                this.Available = available;
            }

            public BindingCandidate Candidate { get; }

            public bool Available { get; }

            public override string ToString()
            {
                string text = string.IsNullOrEmpty(this.Candidate.Units)
                    ? this.Candidate.Name
                    : this.Candidate.Name + " (" + this.Candidate.Units + ")";
                return this.Available ? text : text + " - already on a gauge";
            }
        }
    }
}
