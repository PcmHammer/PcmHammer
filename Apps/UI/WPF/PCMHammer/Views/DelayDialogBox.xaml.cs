using PcmHacking;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for DelayDialogBox.xaml
    /// </summary>
    public partial class DelayDialogBox : Window
    {
        private readonly DelayDialogBoxViewModel _viewModel;

        public DelayDialogBox()
        {
            InitializeComponent();
            _viewModel = new DelayDialogBoxViewModel();
            DataContext = _viewModel;
            _viewModel.RequestClose += Cancel;
        }

        private void Cancel()
        {
            DialogResult = true;
            Close();
        }
    }
}
