using System.Xml.Linq;
using Microsoft.UI.Xaml.Media.Animation;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record OtherFunctionsModel//INavigator navigator, VehicleService vehicleService)
{
//    private System.Threading.Timer? timer;

    public IState<string> Vin => State<string>.Value(this, () => string.Empty);
    public IState<string> OperatingSystemId => State<string>.Value(this, () => string.Empty);
/*
    public Task Start()
    {
        this.timer = new System.Threading.Timer(
                    UpdateProperties,
                    state: null,
                    dueTime: 0,
                    period: 2000);

        return Task.CompletedTask;
    }

    public Task Stop()
    {
        this.timer?.Dispose();
        return Task.CompletedTask;
    }

    public async void UpdateProperties(object? state)
    {
        await this.Vin.SetAsync(string.Empty);
        await this.Vin.SetAsync(await this.GetVin(CancellationToken.None));

        await this.OperatingSystemId.SetAsync(string.Empty);
        await this.OperatingSystemId.SetAsync(await this.GetOperatingSystemId(CancellationToken.None));
    }
    
    private async ValueTask<string> GetVin(CancellationToken cancellationToken)
    {
        var vinResponse = await this.vehicleService.Vehicle.QueryVin();
        if (vinResponse.Status != ResponseStatus.Success)
        {
            return "VIN query failed: " + vinResponse.Status.ToString();
        }
        return vinResponse.Value;
    }

    private async ValueTask<string> GetOperatingSystemId(CancellationToken cancellationToken)
    {
        var response = await this.vehicleService.Vehicle.QueryOperatingSystemId(cancellationToken);
        if (response.Status != ResponseStatus.Success)
        {
            return "Operating system ID query failed: " + response.Status.ToString();
        }
        return response.ToString();
    }

    public async Task GoToFullRead()
    {
        await this.navigator.NavigateViewModelAsync<HelpModel>(this);
    }

    public async Task GoToChangeVin()
    {
        await this.navigator.NavigateViewModelAsync<HelpModel>(this);
    }
    
    public async Task GoToCrankRelearn()
    {
        await this.navigator.NavigateViewModelAsync<HelpModel>(this);
    }
    
    public Task ResetCodes()
    {
        return Task.CompletedTask;
    }
*/
}
