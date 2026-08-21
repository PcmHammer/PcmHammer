// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.UI.Dispatching;
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
public partial record OtherFunctionsModel
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

    /// <summary>The software module identifiers a CAN PCM reports; empty for a VPW PCM.</summary>
    public IListState<CanIdentification.Item> SoftwareModules => ListState<CanIdentification.Item>.Empty(this);

    /// <summary>Drives the visibility of the software module list, which only a CAN PCM fills in.</summary>
    public IState<bool> HasSoftwareModules => State<bool>.Value(this, () => false);

    public OtherFunctionsModel(
        INavigator navigator, 
        IConnectionService vehicleService,
        LoggerAdapter logger,
        DispatcherQueue dispatcherQueue)
    {
        this.navigator = navigator;
        this.connectionService = vehicleService;
        this.progressLogger = logger;
        this.dispatcherQueue = dispatcherQueue;

        // Loaded="{Binding IdentifyPcm}"
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
            if (await this.IdentifyPcm(cancellation.Token))
            {
                break;
            }

            await Task.Delay(1000);
        }
    }

    [Command]
    private async Task<bool> IdentifyPcm(CancellationToken cancellationToken)
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Reading...", true))
            {
                Vehicle vehicle = lease.Vehicle;

                // One shared flow detects the bus (VPW or CAN) and reads the identification; this
                // model only displays and logs what it returns.
                PcmIdentity? identity = await vehicle.ReadIdentity(cancellationToken);
                if (identity == null)
                {
                    this.progressLogger.AddUserMessage("No PCM detected.");
                    return false;
                }

                foreach (string line in identity.Lines)
                {
                    this.progressLogger.AddUserMessage(line);
                }

                await this.Description.SetAsync(identity.Description);
                await this.Vin.SetAsync(identity.Vin);
                await this.CalibrationId.SetAsync(identity.CalibrationId);
                await this.HardwareId.SetAsync(identity.HardwareId);
                await this.SerialNumber.SetAsync(identity.SerialNumber);
                await this.BroadcastCode.SetAsync(identity.BroadcastCode);
                await this.Mec.SetAsync(identity.Mec);

                // Only a CAN PCM reports these; the list is empty (and hidden) for a VPW PCM.
                ImmutableList<CanIdentification.Item> modules = identity.SoftwareModules.ToImmutableList();
                await this.SoftwareModules.Update(updater: existing => modules, ct: cancellationToken);
                await this.HasSoftwareModules.SetAsync(modules.Count > 0);

                return true;
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
        await this.SoftwareModules.Update(updater: existing => ImmutableList<CanIdentification.Item>.Empty, ct: CancellationToken.None);
        await this.HasSoftwareModules.SetAsync(false);
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

    public async Task GoToRead()
    {
        await this.navigator.NavigateViewModelAsync<ReadModel>(this);
    }

    public async Task GoToDumpRam()
    {
        await this.navigator.NavigateViewModelAsync<DumpRamModel>(this);
    }

    public async Task GoToVerify()
    {
        // See comments in MenuModel.GoToWrite()
        WriteModel.WriteType = WriteType.Compare;
        await this.navigator.NavigateViewModelAsync<WriteModel>(this);

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
        await this.IdentifyPcm(CancellationToken.None);
    }
}
