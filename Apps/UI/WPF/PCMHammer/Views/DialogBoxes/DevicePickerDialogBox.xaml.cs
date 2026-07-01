using PcmHacking;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for DevicePickerDialogBox.xaml
    /// </summary>
    public partial class DevicePickerDialogBox : Window
    {
        private readonly DevicePickerViewModel _viewModel;
        private Device? _device;
        public Device? SelectedDevice
        { 
            get => _device;
            set => _device = value; 
        }
        private bool _enable4xReadWrite;
        public bool Enable4xReadWrite
        {
            get => _enable4xReadWrite;
            set => _enable4xReadWrite = value;
        }

        public DevicePickerDialogBox(ILogger logger)
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
            SelectedDevice = _viewModel.SelectedDevice;
            Enable4xReadWrite = _viewModel.Enable4xReadWrite;
            DialogResult = true;
            Close();
        }
    }
}
