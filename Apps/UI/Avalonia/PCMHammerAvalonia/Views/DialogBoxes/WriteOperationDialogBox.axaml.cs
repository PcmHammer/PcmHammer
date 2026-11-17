using Avalonia.Controls;
using PcmHacking;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class WriteOperationDialogBox : Window
{
    private readonly WriteTypeViewModel _viewModel;

    public PcmType SelectedPCMType { get; set; }
    public WriteType SelectedWriteType { get; set; }
    public bool SuppressOSIDWarning { get; set; }

    public WriteOperationDialogBox()
    {
        InitializeComponent();

        _viewModel = new WriteTypeViewModel();
        DataContext = _viewModel;

        // The combo boxes now live in the hosted WriteOperationView; seed the selected values on the
        // view model directly rather than reaching into named controls.
        _viewModel.SelectedPCMType = _viewModel.PCMTypes[0];
        _viewModel.SelectedWriteType = _viewModel.WriteTypes[0];

        _viewModel.RequestClose += () => Close(false);
        _viewModel.RequestAcceptandClose += AcceptAndClose;
    }

    private void AcceptAndClose()
    {
        SelectedPCMType = _viewModel.SelectedPCMType;
        SelectedWriteType = _viewModel.SelectedWriteType;
        SuppressOSIDWarning = _viewModel.SuppressOSIDWarning;
        Close(true);
    }
}
