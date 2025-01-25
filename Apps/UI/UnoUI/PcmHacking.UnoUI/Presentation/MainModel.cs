using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record MainModel
{
    private IMessenger messenger;
    private INavigator navigator;
    private ISettingsService settingsService;
    private IVehicleService vehicleService;

    public MainModel(
        IStringLocalizer localizer,
        INavigator navigator,
        IMessenger messenger,
        ISettingsService settingsService,
        IVehicleService vehicleService)
    {
        this.messenger = messenger;
        this.navigator = navigator;
        this.settingsService = settingsService;
        this.vehicleService = vehicleService;
        this.Title = localizer["ApplicationName"];

        messenger.Register<SettingsChangedMessage>(this, async (sender, message) =>
        {
            await this.UpdateDisplayedSettings(CancellationToken.None);
        });

        vehicleService.ConnectionState.ForEach((state, ct) => this.UpdateDisplayedConnectionState(ct));

        // This is a bit of a hack. We need to update the displayed settings when the page is loaded.
        // They're async, but constructors can't be async. So we fire and forget.
        this.vehicleService.TryConnect(this.settingsService.GetCurrentSettings());
        //this.UpdateDisplayedSettings(CancellationToken.None);
        //this.UpdateDisplayedConnectionState(CancellationToken.None);
    }

    public string? Title { get; }
    
    [Command]
    public async ValueTask GoBack()
    {
        await this.navigator.GoBack(this);
    }

    public IState<string> SerialPortName => State<string>.Value(this, () => string.Empty);

    public IState<string> DeviceName => State<string>.Value(this, () => string.Empty);

    public IState<string> ConnectionState => State<string>.Value(this, () => string.Empty);

    public IState<string> ConnectionError => vehicleService.ConnectionError;

    public IState<string> OperatingSystemId => vehicleService.OperatingSystemId;

    public IState<string> Voltage => vehicleService.Voltage;

    private async ValueTask UpdateDisplayedSettings(CancellationToken ct)
    {
        if (await this.settingsService.GetObd2DeviceCategory(ct) == "J2534")
        {
            await this.SerialPortName.SetAsync("Not Used");
            await this.DeviceName.SetAsync(await this.settingsService.GetJ2534DeviceName(ct));
        }
        else
        {
            await this.SerialPortName.SetAsync(await this.settingsService.GetObd2SerialPortName(ct));
            await this.DeviceName.SetAsync(await this.settingsService.GetObd2SerialDeviceName(ct));
        }
    }

    private async ValueTask UpdateDisplayedConnectionState(CancellationToken ct)
    {
        switch(await this.vehicleService.ConnectionState.Value())
        {
            case ConnectionStates.NotConfigured:
                await this.ConnectionState.SetAsync("Not Configured");
                break;
            case ConnectionStates.NotConnected:
                await this.ConnectionState.SetAsync("Not Connected");
                break;
            case ConnectionStates.Connecting:
                await this.ConnectionState.SetAsync("Connecting");
                break;
            case ConnectionStates.Connected:
                await this.ConnectionState.SetAsync("Connected");
                break;
            case ConnectionStates.InUse:
                await this.ConnectionState.SetAsync("In Use");
                break;
        }
    }
}
