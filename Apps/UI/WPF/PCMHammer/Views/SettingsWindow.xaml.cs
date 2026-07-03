using PCMHammer.Services;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;
        public SettingsWindow(FileDialogService fileDialogService)
        {
            InitializeComponent();
            _viewModel = new SettingsViewModel(fileDialogService);
            DataContext = _viewModel;
            // Handle Close/Cancel (Discards changes since SaveSettings won't be called)
            _viewModel.RequestClose += () =>
            {
                DialogResult = false;
                Close();
            };
            // Handle Accept (Saves changes and signals success)
            _viewModel.RequestAcceptandClose += () =>
            {
                DialogResult = true;
                Close();
            };
        }
    }
}
