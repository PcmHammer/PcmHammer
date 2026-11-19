using Avalonia.Controls;

namespace PCMHammerAvalonia.Views.DialogBoxes;

/// <summary>
/// The Crank Relearn content, factored out so it can be hosted in a Window (desktop) or in a
/// ContentDialog overlay (Android single-view, which has no Window). Binds to a
/// <see cref="ViewModels.CrankRelearnViewModel"/> supplied as its DataContext.
/// </summary>
public partial class CrankRelearnView : UserControl
{
    public CrankRelearnView()
    {
        InitializeComponent();
    }
}
