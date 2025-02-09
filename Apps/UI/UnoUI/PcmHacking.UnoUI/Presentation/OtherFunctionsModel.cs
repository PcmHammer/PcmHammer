using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using System;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record OtherFunctionsModel
{
    private const string defaultClearCodesButtonText = "Clear Trouble Codes";
    private const string defaultValue = "---";
    private readonly DispatcherQueue dispatcherQueue;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly PcmHacking.ILogger progressLogger;

    public IState<string> ResetCodesButtonText => State<string>.Value(this, () => defaultClearCodesButtonText);
    public IState<string> Description => State<string>.Value(this, () => defaultValue);
    public IState<string> Vin => State<string>.Value(this, () => defaultValue);
    public IState<string> CalibrationId => State<string>.Value(this, () => defaultValue);
    public IState<string> HardwareId => State<string>.Value(this, () => defaultValue);
    public IState<string> SerialNumber => State<string>.Value(this, () => defaultValue);
    public IState<string> BroadcastCode => State<string>.Value(this, () => defaultValue);
    public IState<string> Mec => State<string>.Value(this, () => defaultValue);

    public OtherFunctionsModel(
        INavigator navigator, 
        IConnectionService vehicleService, 
        PcmHacking.ILogger progressLogger,
        DispatcherQueue dispatcherQueue)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.progressLogger = progressLogger;
        this.dispatcherQueue = dispatcherQueue;

        // Loaded="{Binding ReadProperties}"
        this.dispatcherQueue.TryEnqueue(async () => {
            await this.ClearDetails();
            await Task.Delay(100);
            await this.ReadProperties(CancellationToken.None);
        });
    }

    [Command]
    private async Task ReadProperties(CancellationToken cancellationToken)
    {
        await this.ClearDetails();
        try
        {
            Vehicle vehicle = await this.connectionService.BeginActivity("Getting Details");

            await this.ReadPropertiesInternal(vehicle, cancellationToken);
        }
        finally
        {
            await this.connectionService.EndActivity();
        }
    }
        
    private async Task ReadPropertiesInternal(Vehicle vehicle, CancellationToken cancellationToken)
    {
        // All VPW PCMs support the VIN query.
        await this.Vin.SetAsync(await this.GetVin(vehicle, cancellationToken));
        await this.Mec.SetAsync(await this.GetMec(vehicle, cancellationToken));

        // The others depend on the operating system.        
        const string notApplicable = "Not Applicable";
        string? osIdString = await this.connectionService.OperatingSystemId.Value();
        uint osId = (uint)0;
        if (uint.TryParse(osIdString ?? "", out osId))
        {
            OSIDInfo pcmInfo = new OSIDInfo(osId);
            await this.Description.SetAsync(pcmInfo.Description);

            if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
            {
                await this.CalibrationId.SetAsync(await this.GetCalibrationId(vehicle, cancellationToken));
                await this.SerialNumber.SetAsync(await this.GetSerialNumber(vehicle, cancellationToken));
            }
            else
            {
                await this.CalibrationId.SetAsync(notApplicable);
                await this.SerialNumber.SetAsync(notApplicable);
            }

            if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P10 && pcmInfo.HardwareType != PcmType.P12 && pcmInfo.HardwareType != PcmType.E54)
            {
                await this.HardwareId.SetAsync(await this.GetHardwareId(vehicle, cancellationToken));
            }
            else
            {
                await this.HardwareId.SetAsync(notApplicable);
            }

            if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P04 && pcmInfo.HardwareType != PcmType.P04_Early && pcmInfo.HardwareType != PcmType.P08)
            {
                await this.BroadcastCode.SetAsync(await this.GetBroadcastCode(vehicle, cancellationToken));
            }
            else
            {
                await this.BroadcastCode.SetAsync(notApplicable);
            }
        }
        else
        {
            string unknown = "Unknown";
            await this.Description.SetAsync(unknown);
            await this.CalibrationId.SetAsync(unknown);
            await this.HardwareId.SetAsync(unknown);
            await this.SerialNumber.SetAsync(unknown);
            await this.BroadcastCode.SetAsync(unknown);
        }
    }

    private async Task ClearDetails()
    {
        await this.CalibrationId.SetAsync(defaultValue);
        await this.SerialNumber.SetAsync(defaultValue);
        await this.HardwareId.SetAsync(defaultValue);
        await this.BroadcastCode.SetAsync(defaultValue);
        await this.Description.SetAsync(defaultValue);
        await this.CalibrationId.SetAsync(defaultValue);
        await this.HardwareId.SetAsync(defaultValue);
        await this.SerialNumber.SetAsync(defaultValue);
        await this.BroadcastCode.SetAsync(defaultValue);
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

    private async ValueTask<string> GetMec(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryMEC();
        if (response.Status != ResponseStatus.Success)
        {
            return "MEC query failed: " + response.Status.ToString();
        }
        return response.Value.ToString();
    }

    public async Task GoToRead()
    {
        await this.navigator.NavigateViewModelAsync<ReadModel>(this);
    }

    public async Task GoToChangeVin()
    {
        await this.navigator.NavigateViewModelAsync<HelpModel>(this);
    }
    
    public async Task GoToCrankRelearn()
    {
        await this.navigator.NavigateViewModelAsync<CrankRelearnModel>(this);
    }
    
    public async Task ResetCodes()
    {
        if (await this.connectionService.TryResetCodes(this.progressLogger))
        {
            await this.ResetCodesButtonText.SetAsync("Success!");
        }
        else
        {
            await this.ResetCodesButtonText.SetAsync("Fail. :(");
            await navigator.ShowMessageDialogAsync(
                sender: this,
                content: "We were not able to clear the trouble codes, but it might work if you try again.",
                title: "Please Try Again",
                buttons: new[] { new DialogAction("Really Not OK") });
        }

        await this.ResetCodesButtonText.SetAsync(defaultClearCodesButtonText);
        await this.ReadProperties(CancellationToken.None);
    }
}
