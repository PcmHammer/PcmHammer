using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for ChangeVinDialogBox.xaml
    /// </summary>
    public partial class ChangeVinDialogBox : Window
    {
        public ChangeVinDialogBox(ChangeVinViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Wire up view closure handlers
            viewModel.RequestCloseOk += () => { DialogResult = true; Close(); };
            viewModel.RequestCloseCancel += () => { DialogResult = false; Close(); };
        }
    }
}
