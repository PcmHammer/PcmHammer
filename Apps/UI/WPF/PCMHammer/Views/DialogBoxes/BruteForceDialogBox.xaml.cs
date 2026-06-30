using PcmHacking;
using PCMHammer.Viewmodels;
using System.ComponentModel;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for BruteForceDialogBox.xaml
    /// </summary>
    public partial class BruteForceDialogBox : Window
    {
        private readonly BruteForceViewModel _viewModel;
        public BruteForceDialogBox(Vehicle vehicle, ILogger logger)
        {
            InitializeComponent();
            _viewModel = new BruteForceViewModel(vehicle, logger);
            DataContext = _viewModel;
            _viewModel.RequestClose += Cancel;
            Closing += BruteForceDialogBox_Closing;
        }

        private void Cancel()
        {
            DialogResult = true;
            Close();
        }

        private void BruteForceDialogBox_Closing(object? sender, CancelEventArgs e)
        {
            if (_viewModel != null && _viewModel.BruteForceRunning)
            {
                e.Cancel = true; // Block immediate exit
                _viewModel.StopCommand.Execute(null); // Signal background cancel loop
                _viewModel.StatusText = "Stopping...";
            }
        }
    }
}
