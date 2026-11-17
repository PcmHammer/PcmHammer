using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class ChangeVinDialogBox : Window
{
    private ChangeVinViewModel? _viewModel;

    public ChangeVinDialogBox() => InitializeComponent();

    public ChangeVinDialogBox(ChangeVinViewModel viewModel) : this() => SetViewModel(viewModel);

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ChangeVinViewModel vm)
            SetViewModel(vm);
    }

    private void SetViewModel(ChangeVinViewModel viewModel)
    {
        // Unsubscribe from previous instance if re-assigned
        _viewModel?.RequestClose -= OnRequestClose;

        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel?.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(bool success)
    {
        Close(success);
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        // Clean up event hook to prevent memory leaks
        _viewModel?.RequestClose -= OnRequestClose;
    }
}