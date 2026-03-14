using Microsoft.Extensions.Logging;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using System.Globalization;
using System.Text;
using Uno.Extensions;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PcmHacking.UnoUI.Presentation;

public record WriteTypeEntity(WriteType Type) : Entity("WriteType");

public partial record WriteModel : IAsyncLogger
{
    private readonly WriteType writeType;
    public static WriteType WriteType;

    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly IDispatcher dispatcher;
    private readonly LoggerAdapter loggerAdapter;
    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Write PCM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);
    public IState<bool> PreferCalibrationWriteEnabled => State<bool>.Value(this, () => true);

    public IState<string> Path => State<string>.Value(this, () => defaultPath);
    public IState<string> UserLog => State<string>.Value(this, () => String.Empty);
    public IState<string> Activity => State<string>.Value(this, () => String.Empty);
    public IState<string> TimeRemaining => State<string>.Value(this, () => String.Empty);
    public IState<string> PercentDone => State<string>.Value(this, () => String.Empty);
    public IState<string> RetryCount => State<string>.Value(this, () => String.Empty);
    public IState<string> Kbps => State<string>.Value(this, () => String.Empty);
    public IState<double> Progress => State<double>.Value(this, () => 0.0);
    public IState<string> StartButtonText => State<string>.Value(this, () => this.GetStartButtonText());
    public IState<string> CalibrationOnlyCheckboxText => State<string>.Value(this, () => this.GetCalibrationOnlyCheckboxText());
    public IState<bool> PreferCalibrationWrite => State<bool>.Value(this, () => false)
        .ForEach((value, ct) => this.PreferCalibrationWriteChanged(value, ct));

    public IState<bool> UseCustomKey => State<bool>.Value(this, () => false).ForEach((value, ct) => UseCustomKeyChanged(value, ct));
    public IState<bool> UseCustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<string> CustomKey => State<string>.Value(this, () => "");
    public IState<bool> CustomKeyEnabled => State<bool>.Value(this, () => true);


    public WriteModel(
        INavigator navigator,
        IConnectionService connectionService,
        LoggerAdapter loggerAdapter,
        ISettingsService settingsService, 
        IDispatcher dispatcher)
    {
        this.navigator = navigator;
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        this.loggerAdapter = loggerAdapter;
        this.writeType = WriteModel.WriteType; // hacky workaround

        // Fire-and-forget initialization
        var _1 = this.Path.SetAsync(this.settingsService.GetLastWrittenFile());
        var _2 = this.PreferCalibrationWrite.SetAsync(this.settingsService.IsCalibrationWritePreferred());
        var _3 = this.UseCustomKey.SetAsync(this.settingsService.GetUseCustomKey());
        var _4 = this.CustomKey.SetAsync(this.settingsService.GetCustomKey());
        var _5 = this.EnableControls(false);        
    }

    private string GetStartButtonText()
    {
        switch (this.writeType)
        {
            case WriteType.TestWrite:
                return "Start Test";
            case WriteType.Compare:
                return "Start Comparison";
            default:
                return "Start Writing";
        }
    }

    private string GetCalibrationOnlyCheckboxText()
    {
        switch (this.writeType)
        {
            case WriteType.TestWrite:
                return "Test Calibration Only (if possible)";
            case WriteType.Compare:
                return "Compare Calibration Only (if possible)";
            default:
                return "Write Calibration Only (if possible)";
        }
    }

    private string GetActivityText()
    {
        switch (this.writeType)
        {
            case WriteType.TestWrite:
                return "Testing";
            case WriteType.Compare:
                return "Verifying";
            default:
                return "Writing";
        }
    }

    private async Task<WriteType> GetActualWriteType()
    {
        switch (this.writeType)
        {
            case WriteType.TestWrite:
                return this.writeType;
            case WriteType.Compare:
                return this.writeType;
            default:
                return await this.PreferCalibrationWrite.Value() ? WriteType.Calibration : WriteType.Full;
        }
    }

    private ValueTask PreferCalibrationWriteChanged(bool preferCalibrationWrite, CancellationToken cancellationToken)
    {
        this.settingsService.ShouldPreferCalibrationWrite(preferCalibrationWrite);
        return ValueTask.CompletedTask;
    }

    private async ValueTask UseCustomKeyChanged(bool value, CancellationToken ct)
    {
        await this.CustomKeyEnabled.SetAsync(value, ct);
        this.settingsService.SetUseCustomKey(value);
    }

    public async Task CustomKeyChanged(string value)
    {
        await this.CustomKey.SetAsync(value);
        this.settingsService.SetCustomKey(value);
    }

    private async Task EnableControls(bool busy)
    {
        await this.StartEnabled.SetAsync(!busy);
        await this.UseCustomKeyEnabled.SetAsync(!busy);
        await this.CustomKeyEnabled.SetAsync(!busy);

        await this.CancelEnabled.SetAsync(busy);        

        if ((this.writeType == WriteType.TestWrite) || (this.writeType == WriteType.Compare))
        {
            await PreferCalibrationWriteEnabled.SetAsync(false);
        }
        else
        {
            await PreferCalibrationWriteEnabled.SetAsync(!busy);
        }
    }

    [Command]
    public async ValueTask Start(CancellationToken cancellationToken)
    {
        await this.EnableControls(true);
        
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
            string activity = this.GetActivityText();
            using (ConnectionLease lease = await this.connectionService.BeginActivity(activity, false))
            using (new LogInterceptor(this.loggerAdapter, this))
            {
                // This results in an error about using the DependencyProperty system on a
                // non-UI thread, which seems like a bug because this code runs on a UI thread.
                // TODO: create a minimal repro, open an issue in the Uno Platform repo.
                //
                // var dialog = new DelayPage();
                // var result = await this.delayDialog.ShowAsync();
                //
                // GetDataAsync doesn't work with ContentDialog. If the user clicks a button, the returned object is null.
                // We do get a valid object if the timer expires, but that's only one of the 3 ways to end the dialog...
                await this.navigator.GetDataAsync<DelayModel, DelayResult>(this, cancellation: cancellationToken);

                // Hacky workaround:
                if (DelayModel.Result?.Proceed == false)
                {
                    await this.AddUserMessage("Write aborted.");
                    return;
                }

                lease.Vehicle.Enable4xReadWrite = this.settingsService.Is4xReadWriteEnabled();
                WriteType actualWriteType = await this.GetActualWriteType();

                string customKeyString = await this.CustomKey.Value() ?? String.Empty;
                uint customKey;
                if (UInt32.TryParse(customKeyString,
                    System.Globalization.NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out customKey) && await this.UseCustomKey.Value())
                {
                    lease.Vehicle.UserDefinedKey = (int)customKey;
                }
                else
                {
                    lease.Vehicle.UserDefinedKey = -1;
                }

                WriteManager writeManager = new(
                    this.loggerAdapter,
                    lease.Vehicle,
                    actualWriteType,
                    this.Alert,
                    this.PromptForYesNo,
                    writeCancellationToken);

                using (new AwayMode())
                {
                    await writeManager.Write(path);
                    await lease.Vehicle.ExitKernel();
                    await lease.Vehicle.ClearTroubleCodes();
                }
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage("Write failed: ");
            await this.AddUserMessage(exception.Message);
            await Task.Delay(1000, cancellationToken);
            await this.AddDebugMessage(exception.ToString());
        }
        finally
        {
            this.tokenSource = null;
            await this.EnableControls(false);
        }
    }

    [Command]
    public async Task ChooseFile()
    {
        try
        {
            await this.StartEnabled.SetAsync(false);
            string path = await this.PromptForFileOpenPath() ?? String.Empty;
            this.settingsService.SetLastWrittenFile(path);
            await this.Path.SetAsync(path);
        }
        finally
        { 
            await this.StartEnabled.SetAsync(true);
        }
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
#if WINDOWS
        nint handle = WindowNative.GetWindowHandle(App.StaticMainWindow);
        InitializeWithWindow.Initialize(openPicker, handle);
#endif
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
