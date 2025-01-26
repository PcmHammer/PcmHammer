using System.Xml.Linq;
using Microsoft.UI.Xaml.Media.Animation;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record OtherFunctionsModel
{
    private INavigator navigator;
    private IVehicleService vehicleService;

    public IState<string> Vin => State<string>.Value(this, () => string.Empty);
    public IState<string> CalibrationId => State<string>.Value(this, () => string.Empty);
    public IState<string> HardwareId => State<string>.Value(this, () => string.Empty);
    public IState<string> SerialNumber => State<string>.Value(this, () => string.Empty);
    public IState<string> BroadcastCode => State<string>.Value(this, () => string.Empty);

    public OtherFunctionsModel(INavigator navigator, IVehicleService vehicleService)
    {
        this.navigator = navigator;
        this.vehicleService = vehicleService;
        this.GetProperties();
    }

    public async Task GetProperties()
    {
        try
        {
            Vehicle? vehicle = await this.vehicleService.TryBeginActivity("Get Details...");
            if (vehicle != null)
            {
                await this.UpdateProperties(vehicle);
            }
        }
        finally
        {
            await this.vehicleService.EndActivity();
        }
    }
        
    public async Task UpdateProperties(Vehicle vehicle)
    {
        CancellationToken ct = CancellationToken.None;
        await this.Vin.SetAsync(string.Empty);
        await this.Vin.SetAsync(await this.GetVin(vehicle, ct));

        await this.CalibrationId.SetAsync(string.Empty);
        await this.CalibrationId.SetAsync(await this.GetCalibrationId(vehicle, ct));

        await this.HardwareId.SetAsync(string.Empty);
        await this.HardwareId.SetAsync(await this.GetHardwareId(vehicle, ct));

        await this.SerialNumber.SetAsync(string.Empty);
        await this.SerialNumber.SetAsync(await this.GetSerialNumber(vehicle, ct));

        await this.BroadcastCode.SetAsync(string.Empty);
        await this.BroadcastCode.SetAsync(await this.GetBroadcastCode(vehicle, ct));
    }
    
    private async ValueTask<string> GetVin(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var vinResponse = await vehicle.QueryVin();
        if (vinResponse.Status != ResponseStatus.Success)
        {
            return "VIN query failed: " + vinResponse.Status.ToString();
        }
        return vinResponse.Value;
    }

    private async ValueTask<string> GetCalibrationId(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryCalibrationId();
        if (response.Status != ResponseStatus.Success)
        {
            return "Calibration ID query failed: " + response.Status.ToString();
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetHardwareId(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryHardwareId();
        if (response.Status != ResponseStatus.Success)
        {
            return "Hardware ID query failed: " + response.Status.ToString();
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetSerialNumber(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QuerySerial();
        if (response.Status != ResponseStatus.Success)
        {
            return "Serial number query failed: " + response.Status.ToString();
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetBroadcastCode(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryBCC();
        if (response.Status != ResponseStatus.Success)
        {
            return "Broadcast code query failed: " + response.Status.ToString();
        }
        return response.Value.ToString();
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
}
