using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record WriteTypeEntity(WriteType Type) : Entity("WriteType");

public partial record WriteModel : IAsyncLogger
{
    private readonly WriteType writeType;

    private readonly IConnectionService connectionService;
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

    public WriteModel(WriteTypeEntity writeTypeEntity, IConnectionService connectionService, IDispatcher dispatcher)
    {
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.progressLogger = new LoggerAdapter(this);
        this.writeType = writeTypeEntity.Type;
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
        CancellationToken readCancellationToken = this.tokenSource.Token;
        try
        {
            await this.connectionService.WriteFlash(
                this.progressLogger,
                this.Alert,
                this.PromptForYesNo,
                this.writeType,
                path,
                readCancellationToken);
        }
        catch (Exception ex)
        {
            await this.AddUserMessage("Write failed: ");
            await this.AddUserMessage(ex.Message);
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
        // Open a Save-As dialog to get the file path
        FileOpenPicker openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add("*.bin");
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
        await this.Activity.SetAsync(String.Empty);
        await this.TimeRemaining.SetAsync(String.Empty);
        await this.PercentDone.SetAsync(String.Empty);
        await this.RetryCount.SetAsync(String.Empty);
        await this.Progress.SetAsync(0.0);
        await this.Kbps.SetAsync(String.Empty);
    }
}
