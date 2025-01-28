using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;


public partial record ReadModel(IVehicleService vehicleService, PcmHacking.ILogger progressLogger, IDispatcher dispatcher)// : PcmHacking.ILogger
{
    public string Title { get { return "Read PCM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);

    public IState<string> UserLog => State<string>.Value(this, () => String.Empty);
    public IState<string> Activity => State<string>.Value(this, () => String.Empty);
    public IState<string> TimeRemaining => State<string>.Value(this, () => String.Empty);
    public IState<string> PercentDone => State<string>.Value(this, () => String.Empty);
    public IState<string> RetryCount => State<string>.Value(this, () => String.Empty);
    public IState<string> Kbps => State<string>.Value(this, () => String.Empty);
    public IState<double> Progress => State<double>.Value(this, () => 0.0);

    [Command]
    public async ValueTask Start(CancellationToken cancellationToken)
    {
        await this.StartEnabled.SetAsync(false);
        await this.CancelEnabled.SetAsync(true);
        await this.vehicleService.ReadFlash(
            this.progressLogger,
            this.Invoke,
            this.PromptForFileSavePath,
            this.PromptForOperatingSystemId,
            this.Alert,
            this.PromptForYesNo,
            cancellationToken);
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        await this.StartEnabled.SetAsync(true);
        await this.CancelEnabled.SetAsync(false);
        return;
    }

    private async Task Invoke(Action action)
    {
        await this.dispatcher.ExecuteAsync(action);
    }

    private async Task<string> PromptForFileSavePath()
    {
        // Open a Save-As dialog to get the file path
        FileSavePicker savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("Binary", new List<string>() { ".bin" });
        savePicker.SuggestedFileName = "Untitled.bin";
        StorageFile file = await savePicker.PickSaveFileAsync();
        if (file == null)
        {
            return null; // TODO: change the return-type to Task<string?> in the refactoring branch.
        }

        return file.Path;
    }

    private Task<uint> PromptForOperatingSystemId()
    {
        // TODO: OS ID dialog box
        return Task.FromResult(12587603u);
    }

    private Task<bool> PromptForYesNo(string message, string title)
    {
        // TODO: Yes/No dialog box
        return Task.FromResult(true);
    }

    private Task Alert(string message, string title)
    {
        // TODO: Alert popup
        return Task.CompletedTask;
    }
    /*
    public async void AddUserMessage(string message)
    {
        await this.UserLog.SetAsync(this.UserLog.Value() + Environment.NewLine + message);
    }
    
    public void AddDebugMessage(string message)
    {
        // TODO: Debug message logging
    }

    public async void StatusUpdateActivity(string activity)
    {
        await this.Activity.SetAsync(activity);
    }

    public async void StatusUpdateTimeRemaining(string remaining)
    {
        await this.TimeRemaining.SetAsync(remaining);
    }

    public async void StatusUpdatePercentDone(string percent)
    {
        await this.PercentDone.SetAsync(percent);
    }

    public async void StatusUpdateRetryCount(string retries)
    {
        await this.RetryCount.SetAsync(retries);
    }

    public async void StatusUpdateProgressBar(double completed, bool visible)
    {
        await this.Progress.SetAsync(completed);
    }

    public async void StatusUpdateKbps(string Kbps)
    {
        await this.Kbps.SetAsync(Kbps);
    }

    public async void StatusUpdateReset()
    {
        await this.Activity.SetAsync(String.Empty);
        await this.TimeRemaining.SetAsync(String.Empty);
        await this.PercentDone.SetAsync(String.Empty);
        await this.RetryCount.SetAsync(String.Empty);
        await this.Progress.SetAsync(0.0);
        await this.Kbps.SetAsync(String.Empty);
    }
    */
}
