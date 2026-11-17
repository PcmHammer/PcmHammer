using Avalonia.Controls;
using Avalonia.Interactivity;
using PcmHacking;
using PCMHammerAvalonia.Services;
using PCMHammerAvalonia.ViewModels;
using System;

namespace PCMHammerAvalonia.Views.DialogBoxes;

public partial class DevicePickerDialogBox : Window
{
    private readonly DevicePickerViewModel? _viewModel;

    public Device? SelectedDevice { get; set; }
    public bool Enable4xReadWrite { get; set; }

    public DevicePickerDialogBox(ILogger logger, SettingsService settingsService)
    {
        InitializeComponent();

        _viewModel = new DevicePickerViewModel(logger, settingsService);
        DataContext = _viewModel;

        _viewModel.RequestClose += Cancel;
        _viewModel.RequestAcceptAndClose += AcceptAndClose;

        Loaded += DevicePicker_Loaded;
    }

    // Default constructor for XAML Previewer
    public DevicePickerDialogBox()
    {
        InitializeComponent();
    }

    private async void DevicePicker_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            try
            {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Device discovery failed: {ex.Message}";
            }
        }
    }

    private void Cancel()
    {
        Close(false);
    }

    private void AcceptAndClose()
    {
        if (_viewModel != null)
        {
            SelectedDevice = _viewModel.SelectedDevice;
            Enable4xReadWrite = _viewModel.Enable4xReadWrite;
        }

        Close(true);
    }
}