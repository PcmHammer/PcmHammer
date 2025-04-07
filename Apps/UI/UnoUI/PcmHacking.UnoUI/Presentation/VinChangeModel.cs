using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using System.Collections.Generic;
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
        this.GetVin();
    }

    async Task GetVin()
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Reading VIN", false))
            {
                Response<string> response = await lease.Vehicle.QueryVin();
                if (response.Status == ResponseStatus.Success)
                {
                    await this.OldVin.SetAsync(response.Value);
                    await this.NewVin.SetAsync(response.Value);
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
    async Task NewVinChanged(string newVin, CancellationToken cancellation)
    {
        if (newVin.Length != 17)
        {
            await this.NewVinStatus.SetAsync("The VIN must be 17 characters long.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }
        if (newVin == (await this.OldVin.Value() ?? string.Empty))
        {
            await this.NewVinStatus.SetAsync("The new VIN is the same as the old VIN.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }

        // TODO: factor this out, in the develop branch - and also add a "switched to 4X" user-message.
/*        if (!VinUtilities.IsAlphaNumeric(newVin))
        {
            await this.NewVinStatus.SetAsync("The new VIN must only contain letters and numbers.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }
        if (!VinUtilities.IsVinChecksumOK(newVin))
        {
            await this.NewVinStatus.SetAsync("The new VIN's checksum is not valid.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }*/

        await this.UpdateButtonEnabled.SetAsync(true);
    }

    [Command]
    async Task UpdateVin()
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Writing VIN", false))
            {
                string newVin = await this.NewVin.Value() ?? throw new InvalidOperationException("New VIN is empty.");
                Response<bool> response = await lease.Vehicle.UpdateVin(newVin);
                if (response.Status == ResponseStatus.Success)
                {
                    await this.OldVin.SetAsync(newVin);
                    this.loggerAdapter.AddUserMessage("VIN write succeeded: " + newVin);
                }
                else
                {
                    this.loggerAdapter.AddUserMessage("VIN write failed");
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