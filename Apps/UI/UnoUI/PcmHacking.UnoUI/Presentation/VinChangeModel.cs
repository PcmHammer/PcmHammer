// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record VinChangeModel
{
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly LoggerAdapter loggerAdapter;

    /// <summary>
    /// The bus and PCM the VIN was read from, so the write goes back over the same one without probing
    /// again. Null until the read succeeds, which is also why the Update button starts disabled.
    /// </summary>
    private VinReadResult? currentVin;

    public IState<string> OldVin => State<string>.Value(this, () => string.Empty);
    public IState<string> OldVinStatus => State<string>.Value(this, () => string.Empty);

    public IState<string> NewVin => State<string>.Value(this, () => string.Empty)
        .ForEach(async (newValue, ct) => await NewVinChanged(newValue ?? string.Empty, ct));
    public IState<string> NewVinStatus => State<string>.Value(this, () => string.Empty);

    public IState<bool> UpdateButtonEnabled => State<bool>.Value(this, () => false);
    public IState<string> UpdateVinStatus => State<string>.Value(this, () => string.Empty);


    public VinChangeModel(
        INavigator navigator,
        IConnectionService connectionService,
        LoggerAdapter loggerAdapter)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));

        var worker = new BackgroundWorker();
        worker.DoWork += async (sender, e) => await this.GetVin();
        worker.RunWorkerAsync();
    }

    async Task GetVin()
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Reading VIN", false))
            {
                // One shared flow detects the bus and reads the VIN over VPW or CAN.
                VinReadResult? result = await lease.Vehicle.ReadVin(CancellationToken.None);
                if (result != null)
                {
                    this.currentVin = result;
                    await this.OldVin.SetAsync(result.Vin);
                    await this.NewVin.SetAsync(result.Vin);
                    await this.OldVinStatus.SetAsync($"Read from {result.Bus}.");
                }
                else
                {
                    this.loggerAdapter.AddUserMessage("VIN read failed");
                    await this.OldVinStatus.SetAsync("VIN read failed");
                }
            }
        }
        catch (Exception exception)
        {
            this.loggerAdapter.AddUserMessage("VIN read failed: ");
            this.loggerAdapter.AddDebugMessage(exception.ToString());
            await this.OldVinStatus.SetAsync("VIN read failed");
        }
    }

    [Command]
    public async Task NewVinChanged(string newVin, CancellationToken cancellation)
    {
        if (newVin == (await this.OldVin.Value() ?? string.Empty))
        {
            await this.NewVinStatus.SetAsync("The new VIN is the same as the old VIN.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }

        // Only the 17-character rule gates the write. A VIN that fails the standard's check digit is
        // still offered, because CAN PCMs are routinely found with one (see VinAssessment).
        VinAssessment assessment = VinAssessment.Of(newVin);
        await this.NewVinStatus.SetAsync(assessment.Message);
        await this.UpdateButtonEnabled.SetAsync(assessment.CanWrite && this.currentVin != null);
    }

    [Command]
    public async ValueTask UpdateVin(CancellationToken cancellationToken)
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Writing VIN", false))
            {
                string newVin = await this.NewVin.Value() ?? throw new InvalidOperationException("New VIN is empty.");

                // The read established the bus and the PCM; reuse them so the write does not probe again.
                if (await lease.Vehicle.WriteVin(
                    newVin, this.currentVin?.Bus, this.currentVin?.PcmInfo, cancellationToken))
                {
                    await this.OldVin.SetAsync(newVin);
                    await this.NewVinStatus.SetAsync("The VIN has been updated.");
                    await this.UpdateVinStatus.SetAsync(Vehicle.VinWriteFollowUp);
                }
                else
                {
                    await this.UpdateVinStatus.SetAsync("VIN write failed");
                }
            }
        }
        catch (Exception exception)
        {
            this.loggerAdapter.AddUserMessage("VIN write failed: ");
            this.loggerAdapter.AddDebugMessage(exception.ToString());
            await this.UpdateVinStatus.SetAsync("VIN write failed");
        }
    }


}
