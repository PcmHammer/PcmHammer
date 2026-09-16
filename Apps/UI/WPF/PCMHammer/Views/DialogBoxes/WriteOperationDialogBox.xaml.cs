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

        /// <param name="detected">
        /// The PCM found by probing the bus before the dialog opened, or null if nothing was detected.
        /// </param>
        public WriteOperationDialogBox(OSIDInfo? detected = null)
        {
            InitializeComponent();
            _viewModel = new WriteTypeViewModel(detected);
            DataContext = _viewModel;
            // The initial selections come from the view model's constructor, not from
            // ComboBox.SelectedIndex here - see WriteTypeViewModel for why that was unreliable.
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
