using System.ComponentModel;
using System.Runtime.CompilerServices;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive;
using Windows.Networking;

namespace PcmHacking.UnoUI.Presentation;

public record CurrentSettings(string DeviceCategory, string Obd2SerialPortName, string Obd2SerialDeviceType, string CanPort);

public partial record SettingsModel
{
    private readonly PcmHacking.ILogger progressLogger;
    private readonly ISettingsService settingsService;
    private readonly ConnectionService connectionService;

    public string Title { get { return "Settings"; } }
    
    public SettingsModel(PcmHacking.ILogger progressLogger, ISettingsService settingsService, ConnectionService connectionService)
    {
        this.progressLogger = progressLogger;
        this.settingsService = settingsService;
        this.connectionService = connectionService;
        
        SelectedObd2Port.ForEach(SelectedObd2PortChanged);
        SelectedCanPort.ForEach(SelectedCanPortChanged);
        SelectedObd2SerialDeviceType.ForEach(SelectedObd2SerialDeviceTypeChanged);
    }
    
    public IListFeed<string> SerialDeviceTypes => ListFeed<string>.Async(ct => this.GetObd2SerialDeviceTypes(ct)).Selection(SelectedObd2SerialDeviceType);
    public IListFeed<string> Obd2Ports => ListFeed.Async<string>(ct => this.GetPortNames(ct)).Selection(SelectedObd2Port);
    public IListFeed<string> CanPorts => ListFeed.Async<string>(ct => this.GetPortNames(ct)).Selection(SelectedCanPort);
    
    public IState<string> SelectedObd2Port => State<string>.Async(this, ct => settingsService.GetObd2SerialPortName(ct));
    public IState<string> SelectedCanPort => State<string>.Async(this, ct => settingsService.GetCanSerialPortName(ct));
    public IState<string> SelectedObd2SerialDeviceType => State<string>.Async(this, ct => settingsService.GetObd2SerialDeviceType(ct));

    private async ValueTask<IImmutableList<string>> GetPortNames(CancellationToken ct)
    {
        string[] portNames = System.IO.Ports.SerialPort.GetPortNames();
        IList<string> portList = new List<string>(portNames);
        portList.Add(MockPort.PortName);
        IImmutableList<string> result = ImmutableList.CreateRange(portList);
        return result;
    }

    private async ValueTask<IImmutableList<string>> GetObd2SerialDeviceTypes(CancellationToken ct)
    {
        string[] deviceTypes = new string[] { "OBDX", "ObdLink Or AllPro", "AVT", };
        IImmutableList<string> result = ImmutableList.CreateRange(deviceTypes);
        return result;
    }

    private ValueTask SelectedObd2PortChanged(string? portName, CancellationToken ct)
    {
        if (portName == null)
        {
            return ValueTask.CompletedTask;
        }

        var currentSettings = new CurrentSettings(
            "Serial", 
            portName, 
            settingsService.GetObd2SerialDeviceType(ct).Result, 
            settingsService.GetCanSerialPortName(ct).Result);

        this.connectionService.Connect(currentSettings);
        return ValueTask.CompletedTask;
    }

    private ValueTask SelectedCanPortChanged(string? portName, CancellationToken ct)
    {
        if (portName == null)
        {
            return ValueTask.CompletedTask;
        }

        var currentSettings = new CurrentSettings(
            "Serial",
            settingsService.GetObd2SerialPortName(ct).Result,
            settingsService.GetObd2SerialDeviceType(ct).Result,
            portName);

        this.connectionService.Connect(currentSettings);
        return ValueTask.CompletedTask;
    }

    private ValueTask SelectedObd2SerialDeviceTypeChanged(string? deviceType, CancellationToken ct)
    {
        if (deviceType == null)
        {
            return ValueTask.CompletedTask;
        }

        var currentSettings = new CurrentSettings(
            "Serial",
            settingsService.GetObd2SerialPortName(ct).Result,
            deviceType,
            settingsService.GetCanSerialPortName(ct).Result);

        this.connectionService.Connect(currentSettings);
        return ValueTask.CompletedTask;
    }
}
