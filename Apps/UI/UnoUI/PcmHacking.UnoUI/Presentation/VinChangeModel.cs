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
    public async Task NewVinChanged(string newVin, CancellationToken cancellation)
    {
        if (newVin.Length != 17)
        {
            await this.NewVinStatus.SetAsync(
                "The VIN must be 17 characters long." + Environment.NewLine +
                $"This is {newVin.Length} characters long.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }

        if (newVin == (await this.OldVin.Value() ?? string.Empty))
        {
            await this.NewVinStatus.SetAsync("The new VIN is the same as the old VIN.");
            await this.UpdateButtonEnabled.SetAsync(false);
            return;
        }

        int invalidCharacterIndex = -1;
        char requiredCheckDigit = 'X';
        if (VinValidator.IsValid(newVin, out invalidCharacterIndex, out requiredCheckDigit))
        {
            await this.NewVinStatus.SetAsync("The VIN is valid. Good!");
            await this.UpdateButtonEnabled.SetAsync(true);
            return;
        }

        await this.UpdateButtonEnabled.SetAsync(false);

        if (invalidCharacterIndex >= 0)
        {
            char invalidCharacter = newVin[invalidCharacterIndex];
            await this.NewVinStatus.SetAsync($"The \"{invalidCharacter}\" at position {invalidCharacterIndex + 1} is not a letter or number.");
            return;
        }

        if (requiredCheckDigit != 'X')
        {
            await this.NewVinStatus.SetAsync($"The VIN check digit on position 9 is incorrect.\nCorrect check digit is: {requiredCheckDigit}");
            return;
        }
    }

    [Command]
    public async ValueTask UpdateVin(CancellationToken cancellationToken)
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
                    await this.NewVinStatus.SetAsync("The VIN has been updated.");
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
