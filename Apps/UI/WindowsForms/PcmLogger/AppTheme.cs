// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PcmHacking
{
    /// <summary>Which colours the app draws itself in.</summary>
    public enum ThemeMode
    {
        /// <summary>Follow the Windows app colour setting.</summary>
        System,

        Light,

        Dark,
    }

    /// <summary>
    /// The colours everything draws with, and applying them to a window.
    /// </summary>
    /// <remarks>
    /// One palette read from everywhere rather than colours chosen at each call site, so a theme is
    /// a table to edit instead of a hunt through the drawing code.
    ///
    /// WinForms on .NET Framework has no dark mode of its own: the colours here are applied control
    /// by control. Some parts are drawn by Windows and do not take a BackColor at all - the arrows
    /// inside a scroll bar, a ComboBox's drop-down list, a ListView's column headers. Those stay
    /// light in dark mode. Chasing them needs either owner-drawing each control or the undocumented
    /// theme API, and neither is worth what it would cost here.
    /// </remarks>
    public static class AppTheme
    {
        /// <summary>The window background, behind everything.</summary>
        public static Color Window { get; private set; } = SystemColors.Control;

        /// <summary>Behind things that take input or hold data: text boxes, grids, lists.</summary>
        public static Color Surface { get; private set; } = SystemColors.Window;

        public static Color Text { get; private set; } = SystemColors.ControlText;

        /// <summary>For labels that are present but not the point.</summary>
        public static Color MutedText { get; private set; } = SystemColors.GrayText;

        /// <summary>Lines: grid rules, axes, borders.</summary>
        public static Color Line { get; private set; } = Color.Gray;

        /// <summary>Buttons, column headers, and anything else raised off the window.</summary>
        public static Color Raised { get; private set; } = SystemColors.Control;

        /// <summary>A gauge face or chart background, which is not an input surface.</summary>
        public static Color PlotBackground { get; private set; } = Color.White;

        /// <summary>The needle, the cursor, the thing being pointed at.</summary>
        public static Color Highlight { get; private set; } = Color.OrangeRed;

        /// <summary>
        /// What a gauge is drawn in when its package left the colour unset.
        /// </summary>
        /// <remarks>
        /// An unset colour arrives as black, which on a white face reads as a drawing fault rather
        /// than a choice - and on a dark face is invisible. Either way it needs replacing.
        /// </remarks>
        public static Color UnsetGauge { get; private set; } = Color.Blue;

        /// <summary>Whether the resolved theme is the dark one, for the odd place that must know.</summary>
        public static bool IsDark { get; private set; }

        /// <summary>
        /// Colours for chart series, in the order they are handed out.
        /// </summary>
        /// <remarks>
        /// Two sets rather than one: the light set is chosen to read against white and several of
        /// those colours disappear against a dark background, so the dark set is the same hues
        /// lifted to stay legible. They have to stay apart from each other as well as from the
        /// background, which is what rules out simply brightening them all.
        /// </remarks>
        private static readonly Color[] LightSeries =
        {
            Color.Teal, Color.Crimson, Color.ForestGreen, Color.DodgerBlue,
            Color.Goldenrod, Color.DarkViolet, Color.OrangeRed, Color.SlateGray,
        };

        private static readonly Color[] DarkSeries =
        {
            Color.FromArgb(0x4E, 0xC9, 0xB0), Color.FromArgb(0xFF, 0x6B, 0x7A),
            Color.FromArgb(0x7D, 0xD8, 0x7D), Color.FromArgb(0x5A, 0xB0, 0xFF),
            Color.FromArgb(0xE8, 0xC0, 0x5A), Color.FromArgb(0xC5, 0x8A, 0xF0),
            Color.FromArgb(0xFF, 0x9A, 0x5A), Color.FromArgb(0xA8, 0xB4, 0xC0),
        };

        // Declared after the two sets it chooses between: a static field initialiser runs in
        // declaration order, so referring to them any earlier would leave this null.
        public static Color[] Series { get; private set; } = LightSeries;

        /// <summary>Work out the palette for a mode and make it current.</summary>
        public static void Select(ThemeMode mode)
        {
            bool dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemPrefersDark());

            IsDark = dark;

            if (dark)
            {
                // Not maximum contrast. Near-white on near-black measures well and is unpleasant to
                // sit in front of, so the text comes down and the backgrounds come up until the
                // difference is comfortable rather than merely large.
                Window = Color.FromArgb(0x2D, 0x2D, 0x30);
                Surface = Color.FromArgb(0x25, 0x25, 0x26);
                Text = Color.FromArgb(0xCC, 0xCC, 0xCC);
                MutedText = Color.FromArgb(0x8A, 0x8A, 0x8A);

                // Rules and borders are structure, not content: they should be found when looked
                // for and otherwise stay out of the way.
                Line = Color.FromArgb(0x3F, 0x3F, 0x46);
                Raised = Color.FromArgb(0x3A, 0x3A, 0x3D);
                PlotBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
                Highlight = Color.FromArgb(0xFF, 0x8A, 0x4A);
                UnsetGauge = Color.FromArgb(0x5A, 0xB0, 0xFF);
                Series = DarkSeries;
            }
            else
            {
                Window = SystemColors.Control;
                Surface = SystemColors.Window;
                Text = SystemColors.ControlText;
                MutedText = SystemColors.GrayText;
                Line = Color.Gray;
                Raised = SystemColors.Control;
                PlotBackground = Color.White;
                Highlight = Color.OrangeRed;
                UnsetGauge = Color.Blue;
                Series = LightSeries;
            }
        }

        /// <summary>One of the series colours, wrapping round if there are more series than colours.</summary>
        public static Color SeriesColor(int index)
        {
            Color[] palette = Series;
            return palette[((index % palette.Length) + palette.Length) % palette.Length];
        }

        /// <summary>Read the mode back from however it was stored, tolerating anything else.</summary>
        public static ThemeMode Parse(string? value)
        {
            return Enum.TryParse(value, true, out ThemeMode mode) ? mode : ThemeMode.System;
        }

        /// <summary>
        /// Whether Windows is set to dark for apps.
        /// </summary>
        /// <remarks>
        /// The registry value is absent on builds that never had the setting, where light is the
        /// right answer. Any failure reading it means the same thing.
        /// </remarks>
        private static bool SystemPrefersDark()
        {
            try
            {
                object? value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    null);

                return value is int light && light == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Paint a control and everything inside it.
        /// </summary>
        /// <remarks>
        /// Recursive and type-aware, because the controls that hold data want the surface colour
        /// while the rest want the window colour, and a few need more than a colour set on them: a
        /// Button ignores BackColor until its FlatStyle allows it, and a TabControl draws its own
        /// tab headers unless it is asked not to.
        /// </remarks>
        public static void Apply(Control control)
        {
            if (control == null)
            {
                return;
            }

            switch (control)
            {
                case TextBoxBase text:
                    text.BackColor = Surface;
                    text.ForeColor = Text;

                    // A single-line border is drawn by Windows in a fixed light colour, which on a
                    // dark surface is a bright rectangle round everything. The surface colour is
                    // enough to show where the box is.
                    text.BorderStyle = IsDark ? BorderStyle.None : BorderStyle.Fixed3D;
                    break;

                case ListView list:
                    list.BackColor = Surface;
                    list.ForeColor = Text;
                    list.BorderStyle = IsDark ? BorderStyle.None : BorderStyle.Fixed3D;

                    // Grid lines are drawn in a system colour that cannot be set, and on a dark
                    // background they come out near-white - the brightest thing on the screen, for
                    // the least important thing on it.
                    list.GridLines = !IsDark;

                    // Headers are drawn by Windows and ignore these colours entirely, so in dark
                    // mode the list draws its own.
                    list.OwnerDraw = IsDark;
                    if (IsDark)
                    {
                        AttachDarkListViewDrawing(list);
                    }

                    break;

                case DataGridView grid:
                    ApplyToGrid(grid);
                    break;

                case ComboBox combo:
                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                    combo.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                    break;

                case Button button:
                    // A themed Button paints over BackColor; flat is what makes it take effect.
                    button.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                    button.BackColor = Raised;
                    button.ForeColor = Text;
                    button.UseVisualStyleBackColor = !IsDark;
                    button.FlatAppearance.BorderColor = Line;
                    break;

                case Form form:
                    form.BackColor = Window;
                    form.ForeColor = Text;
                    ApplyToTitleBar(form);
                    break;

                case TabControl tabs:
                    tabs.BackColor = Window;
                    tabs.ForeColor = Text;

                    // The tab strip is drawn by Windows and takes no notice of BackColor, so in
                    // dark mode the control draws its own tabs.
                    tabs.DrawMode = IsDark ? TabDrawMode.OwnerDrawFixed : TabDrawMode.Normal;

                    // Shapes stay normal. The strip behind the tabs is dealt with by taking the
                    // control off visual styles (see ApplyNativeTheme), which is the documented way
                    // and does not cost the tab shape the way flat buttons did.
                    tabs.Appearance = TabAppearance.Normal;

                    if (IsDark)
                    {
                        AttachDarkTabDrawing(tabs);
                    }

                    break;

                case TabPage page:
                    // UseVisualStyleBackColor must go off for BackColor to be used at all: left on,
                    // the page is painted with the theme's own near-white tab body, which is why a
                    // light window had a white page floating inside a grey frame.
                    page.UseVisualStyleBackColor = false;
                    page.BackColor = Window;
                    page.ForeColor = Text;
                    break;

                case GroupBox group:
                    // A GroupBox draws its caption in ForeColor, so this is what keeps the box
                    // titles readable rather than near-black on near-black.
                    group.BackColor = Window;
                    group.ForeColor = Text;
                    break;

                case DashboardView dash:
                    // Drawn, not composed of controls: it wants the plot colour, and a repaint.
                    dash.BackColor = PlotBackground;
                    dash.Invalidate();
                    break;

                case MonitorView monitor:
                    monitor.BackColor = PlotBackground;
                    monitor.Invalidate();
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = label.Enabled ? Text : MutedText;
                    break;

                default:
                    control.BackColor = Window;
                    control.ForeColor = Text;
                    break;
            }

            ApplyNativeTheme(control);

            foreach (Control child in control.Controls)
            {
                Apply(child);
            }
        }

        /// <summary>
        /// Ask Windows to draw this control's own parts dark, picking the class it answers to.
        /// </summary>
        /// <remarks>
        /// The scroll bars, drop-down lists and borders that no colour property reaches. Which
        /// theme class a control wants differs by control, and the wrong one does nothing at all,
        /// which is why this is a table rather than one call for everything.
        /// </remarks>
        private static void ApplyNativeTheme(Control control)
        {
            if (!control.IsHandleCreated)
            {
                return;
            }

            switch (control)
            {
                case TabControl _:
                    // Off visual styles entirely: the strip is painted by the theme otherwise, and
                    // no colour set on the control survives it.
                    NativeDarkMode.DisableVisualStyles(control.Handle, IsDark);
                    break;

                case TextBoxBase _:
                case ComboBox _:
                    NativeDarkMode.UseTheme(control.Handle, IsDark, NativeDarkMode.CommonFileDialog);
                    break;

                case ListView _:
                case TreeView _:
                case DataGridView _:
                case ScrollBar _:
                    // Named sub-id, because the scroll bar is non-client: the class on its own does
                    // not reach it.
                    NativeDarkMode.UseTheme(
                        control.Handle, IsDark, NativeDarkMode.Explorer, "ScrollBar");
                    break;

                default:
                    NativeDarkMode.UseTheme(control.Handle, IsDark, NativeDarkMode.Explorer);
                    break;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr window, int attribute, ref int value, int size);

        /// <summary>Ask the window manager for a dark title bar (Windows 10 1809 and later).</summary>
        /// <remarks>
        /// The title bar belongs to Windows, not to the form, so no colour set here can reach it.
        /// The attribute number changed during Windows 10's life: 20 is the documented one, 19 is
        /// what the builds between 1809 and 1903 answer to. Trying both costs nothing, and on a
        /// build that knows neither the call fails harmlessly and the bar stays light.
        /// </remarks>
        private static void ApplyToTitleBar(Form form)
        {
            if (!form.IsHandleCreated)
            {
                return;
            }

            // Before the title bar, because this is what the rest of the window's non-client
            // drawing follows - and what reaches menus and the common dialogs.
            NativeDarkMode.SetAppMode(IsDark);
            NativeDarkMode.AllowForWindow(form.Handle, IsDark);

            int enabled = IsDark ? 1 : 0;

            try
            {
                if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
                // Older than the window manager this needs. Nothing to do about it.
            }
        }

        /// <summary>Tab strips that have already been given the dark drawing handler.</summary>
        private static readonly HashSet<TabControl> DarkTabs = new HashSet<TabControl>();

        /// <summary>Draw the tab headers, which Windows otherwise paints in the system theme.</summary>
        private static void AttachDarkTabDrawing(TabControl tabs)
        {
            if (!DarkTabs.Add(tabs))
            {
                return;
            }

            tabs.DrawItem += (sender, e) =>
            {
                TabPage page = tabs.TabPages[e.Index];
                bool selected = tabs.SelectedIndex == e.Index;

                using (Brush back = new SolidBrush(selected ? Window : Raised))
                {
                    e.Graphics.FillRectangle(back, e.Bounds);
                }

                using (Pen edge = new Pen(Line))
                {
                    e.Graphics.DrawRectangle(
                        edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                }

                // Flat tabs are all the same shape, so the selected one is marked rather than
                // raised - otherwise only the text weight says which page you are on.
                if (selected)
                {
                    using (Pen marker = new Pen(Highlight, 2f))
                    {
                        e.Graphics.DrawLine(
                            marker,
                            e.Bounds.Left + 1,
                            e.Bounds.Bottom - 1,
                            e.Bounds.Right - 2,
                            e.Bounds.Bottom - 1);
                    }
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    page.Text,
                    tabs.Font,
                    e.Bounds,
                    selected ? Text : MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }

        /// <summary>Lists that have already been given the dark drawing handlers.</summary>
        private static readonly HashSet<ListView> DarkLists = new HashSet<ListView>();

        /// <summary>
        /// Draw a ListView's headers and rows, since Windows will not do it in these colours.
        /// </summary>
        /// <remarks>
        /// Attached once per list. The handlers read the palette when they run rather than closing
        /// over it, so a later theme change needs no re-wiring; only the OwnerDraw flag moves.
        /// </remarks>
        private static void AttachDarkListViewDrawing(ListView list)
        {
            if (!DarkLists.Add(list))
            {
                return;
            }

            list.DrawColumnHeader += (sender, e) =>
            {
                using (Brush back = new SolidBrush(Raised))
                {
                    e.Graphics.FillRectangle(back, e.Bounds);
                }

                using (Pen edge = new Pen(Line))
                {
                    e.Graphics.DrawLine(edge, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(edge, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    e.Header?.Text ?? string.Empty,
                    e.Font ?? list.Font,
                    Rectangle.Inflate(e.Bounds, -4, 0),
                    Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };

            // Rows are left to the default painting, which honours BackColor and ForeColor; only
            // the selection needs saying, because its system colours ignore both.
            list.DrawItem += (sender, e) => e.DrawDefault = true;

            list.DrawSubItem += (sender, e) => e.DrawDefault = true;
        }

        /// <summary>Grids that have already been given the dark drawing handler.</summary>
        private static readonly HashSet<DataGridView> DarkGrids = new HashSet<DataGridView>();

        /// <summary>
        /// Draw the parts of a grid that ignore cell styles: headers, check boxes, drop-downs.
        /// </summary>
        /// <remarks>
        /// Row backgrounds honour DefaultCellStyle, which is why the rows went dark and these three
        /// did not. A column header is painted from the system theme, and a check box or combo cell
        /// renders its glyph through the visual styles regardless of what the cell style says, so
        /// the only way to colour them is to draw them.
        ///
        /// Attached once and left in place; the handler does nothing in light mode, where the
        /// control's own drawing is what is wanted.
        /// </remarks>
        private static void AttachDarkGridDrawing(DataGridView grid)
        {
            if (!DarkGrids.Add(grid))
            {
                return;
            }

            grid.CellPainting += (sender, e) =>
            {
                if (!IsDark || e.ColumnIndex < 0)
                {
                    return;
                }

                if (e.RowIndex == -1)
                {
                    PaintGridHeader(e);
                    return;
                }

                switch (grid.Columns[e.ColumnIndex])
                {
                    case DataGridViewCheckBoxColumn _:
                        PaintGridCheckBox(e);
                        break;

                    case DataGridViewComboBoxColumn _:
                        PaintGridComboBox(e);
                        break;
                }
            };
        }

        private static void PaintGridHeader(DataGridViewCellPaintingEventArgs e)
        {
            using (Brush back = new SolidBrush(Raised))
            {
                e.Graphics.FillRectangle(back, e.CellBounds);
            }

            using (Pen edge = new Pen(Line))
            {
                e.Graphics.DrawLine(
                    edge, e.CellBounds.Right - 1, e.CellBounds.Top,
                    e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
                e.Graphics.DrawLine(
                    edge, e.CellBounds.Left, e.CellBounds.Bottom - 1,
                    e.CellBounds.Right, e.CellBounds.Bottom - 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                e.FormattedValue?.ToString() ?? string.Empty,
                e.CellStyle?.Font ?? SystemFonts.DefaultFont,
                Rectangle.Inflate(e.CellBounds, -4, 0),
                Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            e.Handled = true;
        }

        private static void PaintGridCheckBox(DataGridViewCellPaintingEventArgs e)
        {
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            Color background = selected
                ? e.CellStyle?.SelectionBackColor ?? Surface
                : e.CellStyle?.BackColor ?? Surface;

            using (Brush back = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(back, e.CellBounds);
            }

            const int Side = 13;
            Rectangle box = new Rectangle(
                e.CellBounds.Left + ((e.CellBounds.Width - Side) / 2),
                e.CellBounds.Top + ((e.CellBounds.Height - Side) / 2),
                Side,
                Side);

            using (Brush face = new SolidBrush(Surface))
            using (Pen edge = new Pen(MutedText))
            {
                e.Graphics.FillRectangle(face, box);
                e.Graphics.DrawRectangle(edge, box);
            }

            if (e.Value is bool ticked && ticked)
            {
                // Drawn rather than glyphed, so it takes the theme's colour like everything else.
                using (Pen tick = new Pen(Text, 2f))
                {
                    e.Graphics.DrawLines(tick, new[]
                    {
                        new Point(box.Left + 3, box.Top + 6),
                        new Point(box.Left + 5, box.Top + 9),
                        new Point(box.Right - 3, box.Top + 3),
                    });
                }
            }

            e.Handled = true;
        }

        private static void PaintGridComboBox(DataGridViewCellPaintingEventArgs e)
        {
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            Color background = selected
                ? e.CellStyle?.SelectionBackColor ?? Surface
                : e.CellStyle?.BackColor ?? Surface;

            using (Brush back = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(back, e.CellBounds);
            }

            Rectangle arrow = new Rectangle(e.CellBounds.Right - 16, e.CellBounds.Top, 16, e.CellBounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                e.FormattedValue?.ToString() ?? string.Empty,
                e.CellStyle?.Font ?? SystemFonts.DefaultFont,
                new Rectangle(
                    e.CellBounds.Left + 2, e.CellBounds.Top, e.CellBounds.Width - 20, e.CellBounds.Height),
                Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            using (Brush glyph = new SolidBrush(MutedText))
            {
                int midX = arrow.Left + (arrow.Width / 2);
                int midY = arrow.Top + (arrow.Height / 2);

                e.Graphics.FillPolygon(glyph, new[]
                {
                    new Point(midX - 4, midY - 2),
                    new Point(midX + 4, midY - 2),
                    new Point(midX, midY + 3),
                });
            }

            e.Handled = true;
        }

        private static void ApplyToGrid(DataGridView grid)
        {
            AttachDarkGridDrawing(grid);

            grid.BackgroundColor = Surface;
            grid.ForeColor = Text;
            grid.GridColor = Line;
            grid.EnableHeadersVisualStyles = !IsDark;

            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = IsDark
                ? Color.FromArgb(0x2D, 0x4F, 0x6B)
                : SystemColors.Highlight;
            grid.DefaultCellStyle.SelectionForeColor = IsDark ? Text : SystemColors.HighlightText;

            grid.ColumnHeadersDefaultCellStyle.BackColor = IsDark
                ? Color.FromArgb(0x33, 0x33, 0x34)
                : SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;

            grid.RowHeadersDefaultCellStyle.BackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
            grid.RowHeadersDefaultCellStyle.ForeColor = Text;

            // A check box or a drop-down inside a cell is drawn with visual styles, which paint
            // their own white box over whatever the cell style says. Flat is what makes those two
            // use the cell's colours instead; the plain text columns need nothing.
            foreach (DataGridViewColumn column in grid.Columns)
            {
                switch (column)
                {
                    case DataGridViewCheckBoxColumn check:
                        check.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                        break;

                    case DataGridViewComboBoxColumn combo:
                        combo.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                        combo.DefaultCellStyle.BackColor = Surface;
                        combo.DefaultCellStyle.ForeColor = Text;
                        break;
                }
            }
        }
    }
}
