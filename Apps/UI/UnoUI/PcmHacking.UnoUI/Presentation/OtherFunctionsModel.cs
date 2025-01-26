using System.Xml.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Animation;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record OtherFunctionsModel
{
    private const string defaultClearCodesButtonText = "Clear Trouble Codes";
    private INavigator navigator;
    private IVehicleService vehicleService;
    private PcmHacking.ILogger progressLogger;

    public IState<string> ResetCodesButtonText => State<string>.Value(this, () => defaultClearCodesButtonText);
    public IState<string> Vin => State<string>.Value(this, () => string.Empty);
    public IState<string> CalibrationId => State<string>.Value(this, () => string.Empty);
    public IState<string> HardwareId => State<string>.Value(this, () => string.Empty);
    public IState<string> SerialNumber => State<string>.Value(this, () => string.Empty);
    public IState<string> BroadcastCode => State<string>.Value(this, () => string.Empty);

    public OtherFunctionsModel(
        INavigator navigator, 
        IVehicleService vehicleService, 
        PcmHacking.ILogger progressLogger,
        DispatcherQueue dispatcherQueue)
    {
        this.navigator = navigator;
        this.vehicleService = vehicleService;
        this.progressLogger = progressLogger;

        // This is deliberately not awaited. Mostly just because you can't use await in a constructor.
#pragma warning disable CS4014
        dispatcherQueue.TryEnqueue(() => this.GetProperties());
        // this.GetProperties();
#pragma warning restore CS4014
    }

    public async Task GetProperties()
    {
        try
        {
            Vehicle vehicle = await this.vehicleService.BeginActivity("Getting Details");
            await this.UpdateProperties(vehicle);
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
    
    public async Task GoToTbd()
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
    
    public async Task ResetCodes()
    {
        try
        {
            Vehicle vehicle = await this.vehicleService.BeginActivity("Clearing Codes");
            await vehicle.ClearTroubleCodes();
            await this.ResetCodesButtonText.SetAsync("Success!");
            await Task.Delay(1000);
            await this.ResetCodesButtonText.SetAsync(defaultClearCodesButtonText);
        }
        catch (Exception exception)
        {
            this.progressLogger.AddUserMessage("Exception while clearing trouble codes.");
            this.progressLogger.AddDebugMessage(exception.ToString());
            await navigator.ShowMessageDialogAsync(
                sender: this,
                content: "We were not able to clear the trouble codes, but it might work if you try again.",
                title: "Please Try Again",
                buttons: new[] { new DialogAction("Really Not OK") });
        }
        finally
        {
            await this.vehicleService.EndActivity();
        }
    }
}
