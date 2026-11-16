using Avalonia.Controls;

namespace PCMHammerAvalonia.Views.DialogBoxes;

/// <summary>
/// The write-type picker content, factored out of <see cref="WriteOperationDialogBox"/> so it can be
/// hosted either in that Window (desktop) or in a ContentDialog overlay (Android single-view, which has
/// no Window). It binds to a <see cref="ViewModels.WriteTypeViewModel"/> supplied as its DataContext.
/// </summary>
public partial class WriteOperationView : UserControl
{
    public WriteOperationView()
    {
        InitializeComponent();
    }
}
