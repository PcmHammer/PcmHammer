using Avalonia.Controls;

namespace PCMHammerAvalonia.Views.DialogBoxes;

/// <summary>
/// The device-picker content, factored out of <see cref="DevicePickerDialogBox"/> so it can be hosted
/// either in that Window (desktop) or in a ContentDialog overlay (Android single-view, which has no
/// Window). It binds to a <see cref="ViewModels.DevicePickerViewModel"/> supplied as its DataContext.
/// </summary>
public partial class DevicePickerView : UserControl
{
    public DevicePickerView()
    {
        InitializeComponent();
    }
}
