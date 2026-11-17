using Avalonia.Controls;
using PcmHacking;
using PCMHammerAvalonia.ViewModels;
using System.Security.AccessControl;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class PcmTypeSelectDialogBox : Window
{
    private readonly PcmSelectViewModel _viewModel;

    public PcmType SelectedPCMType { get; set; }

    public PcmTypeSelectDialogBox()
    {
        InitializeComponent();

        _viewModel = new PcmSelectViewModel();
        DataContext = _viewModel;

        PCMTypeComboBox.SelectedIndex = 0;

        _viewModel.RequestClose += () => Close(false);
        _viewModel.RequestAcceptandClose += AcceptAndClose;
    }

    private void AcceptAndClose()
    {
        SelectedPCMType = _viewModel.SelectedPCMType;
        Close(true);
    }
}