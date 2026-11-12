using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PcmHacking;

namespace PCMLoggerAvalonia.Views;

/// <summary>
/// Choose the serial port, interface type (or auto-detect) and 4x option, WinForms "Select OBD2 Device"
/// style. On OK, <see cref="Accepted"/> is true and the choices are readable from the properties.
/// </summary>
public partial class DeviceSelectDialog : Window
{
    private const string AutoDetect = "Auto-detect";

    public bool Accepted { get; private set; }
    public string? PortName { get; private set; }
    public string? DeviceType { get; private set; }   // null = auto-detect
    public bool Enable4x { get; private set; }

    public DeviceSelectDialog()
    {
        InitializeComponent();
    }

    public DeviceSelectDialog(IReadOnlyList<SerialPortInfo> ports, string? currentPort, string? currentDeviceType, bool enable4x)
        : this()
    {
        this.PortBox.ItemsSource = ports;
        this.PortBox.SelectedItem = ports.FirstOrDefault(p => p.PortName == currentPort) ?? ports.FirstOrDefault();

        List<string> types = new() { AutoDetect, ElmDevice.DeviceType, AvtDevice.DeviceType, OBDXProDevice.DeviceType };
        this.DeviceTypeBox.ItemsSource = types;
        this.DeviceTypeBox.SelectedItem = currentDeviceType != null && types.Contains(currentDeviceType) ? currentDeviceType : AutoDetect;

        this.FourXCheck.IsChecked = enable4x;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => this.Close();

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        this.PortName = (this.PortBox.SelectedItem as SerialPortInfo)?.PortName;
        string? type = this.DeviceTypeBox.SelectedItem as string;
        this.DeviceType = type == AutoDetect ? null : type;
        this.Enable4x = this.FourXCheck.IsChecked == true;
        this.Accepted = true;
        this.Close();
    }
}
