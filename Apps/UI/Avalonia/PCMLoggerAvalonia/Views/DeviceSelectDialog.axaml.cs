using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PcmHacking;

namespace PCMLoggerAvalonia.Views;

/// <summary>
/// Choose the interface, WinForms "Select OBD2 Device" style: a serial port + type, or a J2534 device,
/// plus the 4x option. J2534 is only offered on Windows (its drivers do not exist on Linux/macOS). One
/// dialog, one codebase - it just shows what the platform can do.
/// </summary>
public partial class DeviceSelectDialog : Window
{
    private const string AutoDetect = "Auto-detect";

    public bool Accepted { get; private set; }
    public bool UseJ2534 { get; private set; }
    public string? PortName { get; private set; }
    public string? DeviceType { get; private set; }       // serial: null = auto-detect
    public string? J2534DeviceType { get; private set; }
    public bool Enable4x { get; private set; }

    public DeviceSelectDialog()
    {
        InitializeComponent();
    }

    public DeviceSelectDialog(
        IReadOnlyList<SerialPortInfo> ports,
        IReadOnlyList<string> j2534Devices,
        bool j2534Supported,
        bool useJ2534,
        string? currentPort,
        string? currentSerialType,
        string? currentJ2534Type,
        bool enable4x)
        : this()
    {
        this.PortBox.ItemsSource = ports;
        this.PortBox.SelectedItem = ports.FirstOrDefault(p => p.PortName == currentPort) ?? ports.FirstOrDefault();

        List<string> types = new() { AutoDetect, ElmDevice.DeviceType, AvtDevice.DeviceType, OBDXProDevice.DeviceType };
        this.DeviceTypeBox.ItemsSource = types;
        this.DeviceTypeBox.SelectedItem = currentSerialType != null && types.Contains(currentSerialType) ? currentSerialType : AutoDetect;

        this.J2534Box.ItemsSource = j2534Devices;
        this.J2534Box.SelectedItem = currentJ2534Type != null && j2534Devices.Contains(currentJ2534Type)
            ? currentJ2534Type
            : j2534Devices.FirstOrDefault();

        this.FourXCheck.IsChecked = enable4x;

        this.J2534Radio.IsEnabled = j2534Supported;
        this.J2534Note.Text = j2534Supported
            ? (j2534Devices.Count == 0 ? "No J2534 devices are installed." : string.Empty)
            : "J2534 is only available on Windows.";

        this.J2534Radio.IsChecked = useJ2534 && j2534Supported;
        this.SerialRadio.IsChecked = !(useJ2534 && j2534Supported);

        this.SerialRadio.IsCheckedChanged += (_, _) => this.UpdatePanels();
        this.UpdatePanels();
    }

    private void UpdatePanels()
    {
        bool serial = this.SerialRadio.IsChecked == true;
        this.SerialPanel.IsEnabled = serial;
        this.J2534Panel.IsEnabled = !serial;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => this.Close();

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        this.UseJ2534 = this.J2534Radio.IsChecked == true;
        if (this.UseJ2534)
        {
            this.J2534DeviceType = this.J2534Box.SelectedItem as string;
        }
        else
        {
            this.PortName = (this.PortBox.SelectedItem as SerialPortInfo)?.PortName;
            string? type = this.DeviceTypeBox.SelectedItem as string;
            this.DeviceType = type == AutoDetect ? null : type;
        }

        this.Enable4x = this.FourXCheck.IsChecked == true;
        this.Accepted = true;
        this.Close();
    }
}
