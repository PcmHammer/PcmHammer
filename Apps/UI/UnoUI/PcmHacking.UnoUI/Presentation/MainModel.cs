using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record MainModel
{
    private INavigator navigator;
    private IVehicleService vehicleService;
    private IMessenger messenger;

    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        INavigator navigator,
        IVehicleService vehicleService,
        IMessenger messenger)
    {
        this.navigator = navigator;
        this.vehicleService = vehicleService;

        this.Title = localizer["ApplicationName"];

        this.messenger = messenger;
        this.messenger.Register<MainModel, VehicleInfo>(this, (recipient, message) => recipient.MessageReceived(message));

        this.VehicleInfo = State.Value(this, () => VehicleService.StartupVehicleInfo);
    }

    public string? Title { get; }

    public IState<string> Name => State<string>.Value(this, () => string.Empty);

    public IState<VehicleInfo> VehicleInfo { get; }

    public void MessageReceived(VehicleInfo vehicleInfo)
    {
        this.VehicleInfo.Update((x) => vehicleInfo, CancellationToken.None);
    }   

    public async Task GoToSecond()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<SecondModel>(this, data: new Entity(name!));
    }

    public async Task GoToSettings()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<SettingsModel>(this);
    }

    public async Task GoToWrite()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<WriteModel>(this);
    }

    public async Task GoToRead()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<ReadModel>(this);
    }
}
