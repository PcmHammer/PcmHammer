using PcmHacking;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for DevicePicker.xaml
    /// </summary>
    public partial class DevicePicker : Window
    {
        private readonly DevicePickerViewModel _viewModel;

        public DevicePicker(ILogger logger)
        {
            InitializeComponent();
            _viewModel = new DevicePickerViewModel(logger);
            DataContext = _viewModel;

            _viewModel.RequestClose += Cancel;
            _viewModel.RequestAcceptAndClose += AcceptAndClose;
            Loaded += DevicePicker_Loaded;
        }

        private async void DevicePicker_Loaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

        private void Cancel()
        {
            DialogResult = false;
            Close();
        }

        private void AcceptAndClose()
        {
            DialogResult = true;
            Close();
        }
    }
}
