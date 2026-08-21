using PcmHacking;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views.DialogBoxes
{
    /// <summary>
    /// Interaction logic for WriteOperationDialogBox.xaml
    /// </summary>
    public partial class WriteOperationDialogBox : Window
    {
        private readonly WriteTypeViewModel _viewModel;

        // Properties
        private PcmType _selectedPCMType;
        public PcmType SelectedPCMType
        {
            get => _selectedPCMType;
            set => _selectedPCMType = value;
        }

        private WriteType _selectedWriteType;
        public WriteType SelectedWriteType
        {
            get => _selectedWriteType;
            set => _selectedWriteType = value;
        }

        public WriteOperationDialogBox()
        {
            InitializeComponent();
            _viewModel = new WriteTypeViewModel();
            DataContext = _viewModel;
            PCMTypeComboBox.SelectedIndex = 0;
            WriteTypeComboBox.SelectedIndex = 0;
            _viewModel.RequestClose += () => Close();
            _viewModel.RequestAcceptandClose += AcceptAndClose;
        }

        private void AcceptAndClose()
        {
            SelectedPCMType = _viewModel.SelectedPCMType;
            SelectedWriteType = _viewModel.SelectedWriteType;
            DialogResult = true;
            Close();
        }
    }
}
