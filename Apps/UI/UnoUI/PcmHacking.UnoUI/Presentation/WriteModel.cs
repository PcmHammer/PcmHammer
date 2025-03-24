using Microsoft.Extensions.Logging;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record WriteTypeEntity(WriteType Type) : Entity("WriteType");

public partial record WriteModel : IAsyncLogger
{
    private readonly WriteType writeType;
    public static WriteType WriteType;

    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly IDispatcher dispatcher;
    private readonly ILogger progressLogger;
    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Write PCM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);

    public IState<string> Path => State<string>.Value(this, () => defaultPath);
    public IState<string> UserLog => State<string>.Value(this, () => String.Empty);
    public IState<string> Activity => State<string>.Value(this, () => String.Empty);
    public IState<string> TimeRemaining => State<string>.Value(this, () => String.Empty);
    public IState<string> PercentDone => State<string>.Value(this, () => String.Empty);
    public IState<string> RetryCount => State<string>.Value(this, () => String.Empty);
    public IState<string> Kbps => State<string>.Value(this, () => String.Empty);
    public IState<double> Progress => State<double>.Value(this, () => 0.0);
    public IState<string> StartButtonText => State<string>.Value(this, () => this.writeType == WriteType.TestWrite ? "Start Test" : "Start Writing");

    public WriteModel(
        IConnectionService connectionService, 
        ISettingsService settingsService, 
        IDispatcher dispatcher) // WriteTypeEntity writeTypeEntity, 
    {
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.progressLogger = new LoggerAdapter(this);
        this.writeType = WriteModel.WriteType; // hacky workaround
    }

    [Command]
    public async ValueTask Start(CancellationToken cancellationToken)
    {
        await this.StartEnabled.SetAsync(false);
        await this.CancelEnabled.SetAsync(true);

        string? path = await this.Path.Value();
        if (string.IsNullOrWhiteSpace(path) || string.Compare(path, defaultPath, StringComparison.OrdinalIgnoreCase) == 0)
        {
            path = await this.PromptForFileOpenPath();
            if (string.IsNullOrWhiteSpace(path) || string.Compare(path, defaultPath, StringComparison.OrdinalIgnoreCase) == 0)
            {
                await this.AddUserMessage("No file selected.");
                await this.StartEnabled.SetAsync(true);
                await this.CancelEnabled.SetAsync(false);
                return;
            }
            await this.Path.SetAsync(path);
        }

        this.tokenSource = new CancellationTokenSource();
        CancellationToken writeCancellationToken = this.tokenSource.Token;
        try
        {
            string activity = this.writeType == WriteType.TestWrite ? "Test Write" : "Writing PCM";
            using (ConnectionLease lease = await this.connectionService.BeginActivity(activity, false))
            {
                lease.Vehicle.Enable4xReadWrite = this.settingsService.Is4xReadWriteEnabled();

                WriteManager writeManager = new(
                    this.progressLogger,
                    lease.Vehicle,
                    this.writeType,
                    this.Alert,
                    this.PromptForYesNo,
                    writeCancellationToken);

                await writeManager.Write(path);

                await lease.Vehicle.ExitKernel();
                await lease.Vehicle.ClearTroubleCodes();
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage("Write failed: ");
            await this.AddUserMessage(exception.Message);
            await Task.Delay(1000);
            await this.AddDebugMessage(exception.ToString());
        }
        finally
        {
            this.tokenSource = null;
            await this.StartEnabled.SetAsync(true);
            await this.CancelEnabled.SetAsync(false);
        }
    }

    [Command]
    public async Task ChooseFile()
    {
        await this.Path.SetAsync(await this.PromptForFileOpenPath());
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        await this.AddUserMessage("Cancelling.");
        this.tokenSource?.Cancel();
        this.tokenSource = null;
    }

    private async Task<string?> PromptForFileOpenPath()
    {
        // Use the standard open-file dialog to get the file path
        // TODO: find/create a touch-friendly file picker
        FileOpenPicker openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".bin");
        StorageFile file = await openPicker.PickSingleFileAsync();
        if (file == null)
        {
            return null;
        }

        return file.Path;
    }

    private Task Alert(string message, string title)
    {
        // TODO: Alert popup
        return Task.CompletedTask;
    }

    private Task<bool> PromptForYesNo(string message, string title)
    {
        // TODO: Yes/No dialog box
        return Task.FromResult(true);
    }

    public async Task AddUserMessage(string message)
    {
        await this.UserLog.SetAsync(this.UserLog.Value() + Environment.NewLine + message);
    }

    public Task AddDebugMessage(string message)
    {
        // TODO: Debug message logging
        return Task.CompletedTask;
    }

    public async Task StatusUpdateActivity(string activity)
    {
        await this.Activity.SetAsync(activity);
    }

    public async Task StatusUpdateTimeRemaining(string remaining)
    {
        await this.TimeRemaining.SetAsync("Estimated Completion: " + remaining);
    }

    public async Task StatusUpdatePercentDone(string percent)
    {
        await this.PercentDone.SetAsync("Progress: " + percent);
    }

    public async Task StatusUpdateRetryCount(string retries)
    {
        if (string.IsNullOrWhiteSpace(retries))
        {
            retries = "None.";
        }

        await this.RetryCount.SetAsync("Retried messages: " + retries);
    }

    public async Task StatusUpdateProgressBar(double completed, bool visible)
    {
        await this.Progress.SetAsync(completed * 100);
    }

    public async Task StatusUpdateKbps(string Kbps)
    {
        await this.Kbps.SetAsync("Connection Speed: " + Kbps);
    }

    public async Task StatusUpdateReset()
    {
        // Not resetting these, so that the user can still see the speed and retry count after the flash completes.
        // await this.RetryCount.SetAsync(String.Empty);
        // await this.Kbps.SetAsync(String.Empty);

        await this.Activity.SetAsync(String.Empty);
        await this.TimeRemaining.SetAsync(String.Empty);
        await this.PercentDone.SetAsync(String.Empty);

        await this.Progress.SetAsync(0.0);
    }
}
