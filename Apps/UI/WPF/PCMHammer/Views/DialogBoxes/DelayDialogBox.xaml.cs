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
        private readonly DelayViewModel _viewModel;

        public DelayDialogBox()
        {
            InitializeComponent();
            _viewModel = new DelayViewModel();
            DataContext = _viewModel;
            _viewModel.RequestClose += Proceed;
            // Whatever dismisses the dialog (Skip, countdown, or the title bar X), the timer must
            // not outlive it.
            Closed += (s, e) => _viewModel.StopTimer();
        }

        /// <summary>
        /// The countdown finished, or the user chose to skip the remaining wait. Both mean "go
        /// ahead", so the caller (which gates the write on ShowDialog() == true) proceeds.
        /// Closing the dialog any other way leaves DialogResult null and cancels the operation.
        /// </summary>
        private void Proceed()
        {
            DialogResult = true;
            Close();
        }
    }
}
