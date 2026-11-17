using Avalonia.Controls;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class DelayDialogBox : Window
{
    private readonly DelayViewModel _viewModel;

    public DelayDialogBox()
    {
        InitializeComponent();

        _viewModel = new DelayViewModel();
        DataContext = _viewModel;

        _viewModel.RequestClose += () => Close(true);
    }
}