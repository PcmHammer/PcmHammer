using Microsoft.UI.Dispatching;
using PcmHacking.ECU;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using System.Threading;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

/// <summary>
/// This page shows some standard properties from the PCM, and has buttons for less-frequently-used features.
/// </summary>
/// <remarks>
/// TO DO:
/// - make sure the crank relearn feature actually works...
/// - VIN change
/// - Add ECT and IAT to the info display?
/// - Make the info display refresh every second? 
/// - add a "verify PCM" button
/// - add a link to an XDF repository?
/// </remarks>
public partial record ControllerFunctionsModel
{
    private const string defaultClearCodesButtonText = "Clear Trouble Codes";
    private const string defaultValue = "---";
    private readonly DispatcherQueue dispatcherQueue;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly LoggerAdapter progressLogger;
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

    public IState<string> ResetCodesButtonText => State<string>.Value(this, () => defaultClearCodesButtonText);
    public IState<string> Description => State<string>.Value(this, () => defaultValue);
    public IState<string> Vin => State<string>.Value(this, () => defaultValue);
    public IState<string> CalibrationId => State<string>.Value(this, () => defaultValue);
    public IState<string> HardwareId => State<string>.Value(this, () => defaultValue);
    public IState<string> SerialNumber => State<string>.Value(this, () => defaultValue);
    public IState<string> BroadcastCode => State<string>.Value(this, () => defaultValue);
    public IState<string> Mec => State<string>.Value(this, () => defaultValue);

    public ControllerFunctionsModel(
        INavigator navigator, 
        IConnectionService vehicleService,
        LoggerAdapter logger,
        DispatcherQueue dispatcherQueue)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.progressLogger = logger;
        this.dispatcherQueue = dispatcherQueue;

        // Loaded="{Binding ReadProperties}"
        this.dispatcherQueue.TryEnqueue(async () => {
            await this.MainLoop();
        });
    }

    public void NavigatedAway()
    {
        cancellation.Cancel();
    }

    private async Task MainLoop()
    {
        await this.ClearDetails();

        while (!cancellation.Token.IsCancellationRequested)
        {
            if (await this.ReadProperties(cancellation.Token))
            {
                break;
            }

            await Task.Delay(1000);
        }
    }

    [Command]
    private async Task<bool> ReadProperties(CancellationToken cancellationToken)
    {
        const int delay = 50;
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Reading...", true))
            {
                Vehicle vehicle = lease.Vehicle;

                // All VPW PCMs support the VIN query.
                await this.Vin.SetAsync(await this.GetVin(vehicle, cancellationToken));
                await Task.Delay(delay);
                await this.Mec.SetAsync(await this.GetMec(vehicle, cancellationToken));
                await Task.Delay(delay);

                // The others depend on the operating system.        
                const string notApplicable = "Not Applicable";
                string? osIdString = await this.GetOperatingSystemId(vehicle, cancellationToken);
                uint osId = (uint)0;
                if (uint.TryParse(osIdString ?? "", out osId))
                {
                    ECUBase pcmInfo = ECUFactory.GetControllerByOSID(osId);
                    await this.Description.SetAsync(pcmInfo.Description);
                    await Task.Delay(delay);

                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.BlackBox)
                    {
                        await this.CalibrationId.SetAsync(await this.GetCalibrationId(vehicle, cancellationToken));
                        await Task.Delay(delay);
                        await this.SerialNumber.SetAsync(await this.GetSerialNumber(vehicle, cancellationToken));
                        await Task.Delay(delay);
                    }
                    else
                    {
                        await this.CalibrationId.SetAsync(notApplicable);
                        await this.SerialNumber.SetAsync(notApplicable);
                        await Task.Delay(delay);
                    }

                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P10 && pcmInfo.HardwareType != PcmType.P12 && pcmInfo.HardwareType != PcmType.E54)
                    {
                        await this.HardwareId.SetAsync(await this.GetHardwareId(vehicle, cancellationToken));
                        await Task.Delay(delay);
                    }
                    else
                    {
                        await this.HardwareId.SetAsync(notApplicable);
                        await Task.Delay(delay);
                    }

                    if (pcmInfo != null && pcmInfo.HardwareType != PcmType.P04 && pcmInfo.HardwareType != PcmType.P04_Early && pcmInfo.HardwareType != PcmType.P08)
                    {
                        await this.BroadcastCode.SetAsync(await this.GetBroadcastCode(vehicle, cancellationToken));
                        await Task.Delay(delay);
                    }
                    else
                    {
                        await this.BroadcastCode.SetAsync(notApplicable);
                        await Task.Delay(delay);
                    }

                    return true;
                }
                else
                {
                    string unknown = "Unknown";
                    await this.Description.SetAsync(unknown);
                    await this.CalibrationId.SetAsync(unknown);
                    await this.HardwareId.SetAsync(unknown);
                    await this.SerialNumber.SetAsync(unknown);
                    await this.BroadcastCode.SetAsync(unknown);
                    return false;
                }
            }
        }
        catch (ConnectionUnavailableException exception)
        {
            this.progressLogger.AddDebugMessage("Other Functions: Connection unavailable while reading properties.");
            this.progressLogger.AddDebugMessage(exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            this.progressLogger.AddDebugMessage("Other Functions: Exception while reading properties.");
            this.progressLogger.AddDebugMessage(exception.Message);
            return false;
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
            throw new Exception("VIN query failed: " + vinResponse.Status.ToString());
        }
        return vinResponse.Value;
    }

    private async ValueTask<string> GetOperatingSystemId(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryOperatingSystemId(cancellationToken);
        if (response.Status != ResponseStatus.Success)
        {
            throw new Exception("Operating system ID query failed: " + response.Status.ToString());
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetCalibrationId(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryCalibrationId();
        if (response.Status != ResponseStatus.Success)
        {
            throw new Exception("Calibration ID query failed: " + response.Status.ToString());
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetHardwareId(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryHardwareId();
        if (response.Status != ResponseStatus.Success)
        {
            throw new Exception("Hardware ID query failed: " + response.Status.ToString());
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetSerialNumber(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QuerySerial();
        if (response.Status != ResponseStatus.Success)
        {
            throw new Exception("Serial number query failed: " + response.Status.ToString());
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetBroadcastCode(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryBCC();
        if (response.Status != ResponseStatus.Success)
        {
            throw new Exception("Broadcast code query failed: " + response.Status.ToString());
        }
        return response.Value.ToString();
    }

    private async ValueTask<string> GetMec(Vehicle vehicle, CancellationToken cancellationToken)
    {
        var response = await vehicle.QueryMEC();
        if (response.Status != ResponseStatus.Success)
        {
            throw new Exception("MEC query failed: " + response.Status.ToString());
        }
        return response.Value.ToString();
    }

    public async Task GoToRead()
    {
        ControllerActionSetupModel.SelectedAction = ControllerActions.Read;
        await this.navigator.NavigateViewModelAsync<ControllerActionSetupModel>(this);
    }

    public async Task GoToWrite()
    {
        ControllerActionSetupModel.SelectedAction = ControllerActions.Write;
        await this.navigator.NavigateViewModelAsync<ControllerActionSetupModel>(this);
    }

    public async Task GoToDumpRam()
    {
        await this.navigator.NavigateViewModelAsync<DumpRamModel>(this);
    }

    public async Task GoToChangeVin()
    {
        await this.navigator.NavigateViewModelAsync<VinChangeModel>(this);
    }
    
    public async Task GoToCrankRelearn()
    {
        await this.navigator.NavigateViewModelAsync<CrankRelearnModel>(this);
    }

    public Task ReadCodes()
    {
        // TODO: navigate to a read-OBD2-codes page
        return Task.CompletedTask;
    }

    public async Task ResetCodes()
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Reset Codes", false))
            {
                Vehicle vehicle = lease.Vehicle;
                try
                {
                    await vehicle.ExitKernel();
                    await vehicle.ClearTroubleCodes();
                    await this.ResetCodesButtonText.SetAsync("Success!");
                    await Task.Delay(1000);
                }
                catch (Exception exception)
                {
                    this.progressLogger.AddUserMessage("Exception while clearing trouble codes.");
                    this.progressLogger.AddDebugMessage(exception.ToString());
                }
            }
            
            await this.ResetCodesButtonText.SetAsync("Success!");
        }
        catch (Exception exception)
        {
            await this.ResetCodesButtonText.SetAsync("Fail. :(");
            this.progressLogger.AddDebugMessage(exception.ToString());
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
