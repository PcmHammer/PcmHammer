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

            // 1. Hook into the Window's Loaded event
            this.Loaded += DevicePicker_Loaded;
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
