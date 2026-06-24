using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for ChangeVINWindow.xaml
    /// </summary>
    public partial class ChangeVINWindow : Window
    {
        public ChangeVINWindow(ChangeVINViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Wire up view closure handlers
            viewModel.RequestCloseOk += () => { DialogResult = true; Close(); };
            viewModel.RequestCloseCancel += () => { DialogResult = false; Close(); };
        }
    }
}
