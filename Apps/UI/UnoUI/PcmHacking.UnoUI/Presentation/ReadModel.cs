using System.Runtime.CompilerServices;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;


public partial record ReadModel(IVehicleService vehicleService)
{
    public string Title { get { return "Read PCM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);

    [Command]
    public async ValueTask Start(CancellationToken ct)
    {
        await this.StartEnabled.SetAsync(false);
        await this.CancelEnabled.SetAsync(true);
        Vehicle? vehicle = null;
        
        try
        {
            // Open a Save-As dialog to get the file path
            FileSavePicker savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Binary", new List<string>() { ".bin" });
            savePicker.SuggestedFileName = "Untitled.bin";
            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            vehicle = await this.vehicleService.BeginActivity("Reading");
            await Read(vehicle, file, ct);
        }
        finally
        {
            if (vehicle != null)
            {
                await this.vehicleService.EndActivity();
            }
        }
        return;
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        await this.StartEnabled.SetAsync(true);
        await this.CancelEnabled.SetAsync(false);
        return;
    }

    private async ValueTask Read(Vehicle vehicle, StorageFile file, CancellationToken ct)
    {
        //await this.vehicleService.Read();
        return;
    }
}
