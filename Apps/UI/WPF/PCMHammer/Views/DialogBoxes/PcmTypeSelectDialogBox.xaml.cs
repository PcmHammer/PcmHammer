using PcmHacking;
using PCMHammer.Viewmodels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PCMHammer.Views.DialogBoxes
{
    /// <summary>
    /// Interaction logic for PcmTypeSelectDialogBox.xaml
    /// </summary>
    public partial class PcmTypeSelectDialogBox : Window
    {
        private readonly PcmSelectViewModel _viewModel;

        // Properties
        private PcmType _selectedPCMType;
        public PcmType SelectedPCMType
        {
            get => _selectedPCMType;
            set => _selectedPCMType = value;
        }

        public PcmTypeSelectDialogBox()
        {
            InitializeComponent();
            _viewModel = new PcmSelectViewModel();
            DataContext = _viewModel;
            PCMTypeComboBox.SelectedIndex = 0;
            _viewModel.RequestClose += () => Close();
            _viewModel.RequestAcceptandClose += AcceptAndClose;
        }

        private void AcceptAndClose()
        {
            SelectedPCMType = _viewModel.SelectedPCMType;
            DialogResult = true;
            Close();
        }
    }
}
