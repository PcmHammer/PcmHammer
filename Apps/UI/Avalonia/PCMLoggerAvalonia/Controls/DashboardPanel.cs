// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Controls;

/// <summary>
/// Arranges each child at the proportional bounds (Left/Top/RelWidth/RelHeight, 0..1) carried by its
/// bound <see cref="GaugeTile"/>, scaling them to the panel's size - the same proportional dashboard
/// layout the WinForms PcmLogger uses, so an imported or default layout keeps its proportions.
/// </summary>
public sealed class DashboardPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

        foreach (Control child in this.Children)
        {
            child.Measure(CellSize(child, w, h));
        }

        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in this.Children)
        {
            if (child.DataContext is GaugeTile tile)
            {
                child.Arrange(new Rect(
                    tile.Left * finalSize.Width,
                    tile.Top * finalSize.Height,
                    tile.RelWidth * finalSize.Width,
                    tile.RelHeight * finalSize.Height));
            }
            else
            {
                child.Arrange(new Rect(child.DesiredSize));
            }
        }

        return finalSize;
    }

    private static Size CellSize(Control child, double w, double h)
    {
        return child.DataContext is GaugeTile tile
            ? new Size(tile.RelWidth * w, tile.RelHeight * h)
            : new Size(w, h);
    }
}
