using Avalonia.Controls;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class UserDefinedKeyDialogBox : Window
{
    private UserDefinedKeyViewModel? _viewModel;

    public UserDefinedKeyDialogBox() => InitializeComponent();

    public UserDefinedKeyDialogBox(UserDefinedKeyViewModel viewModel) : this() => SetViewModel(viewModel);

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is UserDefinedKeyViewModel vm)
        {
            SetViewModel(vm);
        }
    }

    private void SetViewModel(UserDefinedKeyViewModel viewModel)
    {
        _viewModel?.RequestClose -= OnRequestClose;

        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel?.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(bool success) => Close(success);

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _viewModel?.RequestClose -= OnRequestClose;
    }
}