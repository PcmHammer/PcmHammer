using Avalonia;
using Avalonia.Controls;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

/// <summary>
/// The bus monitor content, factored out so it can be hosted in a Window (desktop) or in a
/// ContentDialog overlay (Android single-view, which has no Window). Binds to a
/// <see cref="BusMonitorViewModel"/> supplied as its DataContext.
/// </summary>
public partial class BusMonitorView : UserControl
{
    public BusMonitorView()
    {
        InitializeComponent();

        // Follow the newest traffic, as the Uno monitor does, unless the user has scrolled up to read.
        TrafficScroller.ScrollChanged += (_, _) =>
        {
            double maxOffset = System.Math.Max(0, TrafficScroller.Extent.Height - TrafficScroller.Viewport.Height);
            if (TrafficScroller.Offset.Y >= maxOffset - 40 || TrafficScroller.Extent.Height <= TrafficScroller.Viewport.Height)
            {
                TrafficScroller.Offset = new Vector(TrafficScroller.Offset.X, double.MaxValue);
            }
        };
    }
}
