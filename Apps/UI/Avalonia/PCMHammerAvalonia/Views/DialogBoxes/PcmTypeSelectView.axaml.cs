using Avalonia.Controls;

namespace PCMHammerAvalonia.Views.DialogBoxes;

/// <summary>
/// PCM-type picker content factored out of <see cref="PcmTypeSelectDialogBox"/> so it can be
/// hosted either in that Window (desktop) or in a ContentDialog overlay (Android single-view).
/// Binds to a <see cref="ViewModels.PcmSelectViewModel"/> supplied as its DataContext.
/// </summary>
public partial class PcmTypeSelectView : UserControl
{
    public PcmTypeSelectView()
    {
        InitializeComponent();
    }
}
