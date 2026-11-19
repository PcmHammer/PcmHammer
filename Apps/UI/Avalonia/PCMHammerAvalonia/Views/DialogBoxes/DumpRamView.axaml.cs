using Avalonia;
using Avalonia.Controls;

namespace PCMHammerAvalonia.Views.DialogBoxes;

/// <summary>
/// The Dump RAM content, factored out so it can be hosted in a Window (desktop) or in a
/// ContentDialog overlay (Android single-view, which has no Window). Binds to a
/// <see cref="ViewModels.DumpRamViewModel"/> supplied as its DataContext.
/// </summary>
public partial class DumpRamView : UserControl
{
    public DumpRamView()
    {
        InitializeComponent();

        // Keep the newest message in view.
        LogScroller.ScrollChanged += (_, _) =>
            LogScroller.Offset = new Vector(LogScroller.Offset.X, double.MaxValue);
    }
}
