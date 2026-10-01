// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.Extensions.Logging;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using System.Globalization;
using System.Text;
using Uno.Extensions;
using Uno.Extensions.Reactive.Commands;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public record WriteTypeEntity(WriteType Type) : Entity("WriteType");

/// <summary>
/// A write type as offered in the write-type list. A plain struct, not a record: State.SetAsync takes
/// only value types, and the reactive generator's proxy for a record assumes reference semantics.
/// </summary>
public readonly struct WriteTypeOption : IEquatable<WriteTypeOption>
{
    public WriteTypeOption(WriteType type)
    {
        this.Type = type;
    }

    public WriteType Type { get; }

    public string DisplayName => OperationOptions.Label(this.Type);

    public bool Equals(WriteTypeOption other) => this.Type == other.Type;

    public override bool Equals(object? obj) => obj is WriteTypeOption other && this.Equals(other);

    public override int GetHashCode() => this.Type.GetHashCode();

    public override string ToString() => this.DisplayName;
}

/// <summary>
/// A PCM type as offered in the override list. <see cref="PcmType.Undefined"/> is the "Auto" entry,
/// which leaves the managers to identify the PCM from its operating system ID.
/// </summary>
public readonly struct PcmTypeOption : IEquatable<PcmTypeOption>
{
    public PcmTypeOption(PcmType type)
    {
        this.Type = type;
    }

    public PcmType Type { get; }

    public string DisplayName => this.Type == PcmType.Undefined ? "Auto (query OSID)" : this.Type.ToString();

    public bool Equals(PcmTypeOption other) => this.Type == other.Type;

    public override bool Equals(object? obj) => obj is PcmTypeOption other && this.Equals(other);

    public override int GetHashCode() => this.Type.GetHashCode();

    public override string ToString() => this.DisplayName;
}

public partial record WriteModel : IAsyncLogger
{
    private readonly WriteType writeType;
    public static WriteType WriteType;

    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IPlatformService platformService;
    private readonly IPromptService promptService;
    private readonly IDispatcher dispatcher;
#pragma warning disable CS0414
    private byte[] _fileBuffer = Array.Empty<byte>();

    /// <summary>Whether the chosen file is a .phz package rather than a raw .bin.</summary>
    private bool _isPackageFile;
#pragma warning restore CS0414

    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Write PCM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);
    /// <summary>Whether the write-type and PCM-type choices can be changed (not while a write runs).</summary>
    public IState<bool> OptionsEnabled => State<bool>.Value(this, () => true);

    public IState<string> Path => State<string>.Value(this, () => defaultPath);
    public IState<string> UserLog => State<string>.Value(this, () => String.Empty);
    public IState<string> Activity => State<string>.Value(this, () => String.Empty);
    public IState<string> TimeRemaining => State<string>.Value(this, () => String.Empty);
    public IState<string> PercentDone => State<string>.Value(this, () => String.Empty);
    public IState<string> RetryCount => State<string>.Value(this, () => String.Empty);
    public IState<string> Kbps => State<string>.Value(this, () => String.Empty);
    public IState<double> Progress => State<double>.Value(this, () => 0.0);
    public IState<string> StartButtonText => State<string>.Value(this, () => this.GetStartButtonText());

    /// <summary>
    /// The write types this PCM supports, per <see cref="OperationOptions"/>. Stops a clone-only CAN
    /// PCM being asked for a calibration write, which otherwise only fails once the kernel is running.
    /// </summary>
    public IListFeed<WriteTypeOption> WriteTypes =>
        ListFeed.Async(ct => this.GetWriteTypes(ct)).Selection(this.SelectedWriteTypeOption);

    public IState<WriteTypeOption> SelectedWriteTypeOption => State<WriteTypeOption>
        .Value(this, () => new WriteTypeOption(WritePlan.DefaultWriteType()))
        .ForEach((value, ct) => this.SelectedWriteTypeChanged(value, ct));

    /// <summary>The PCM type override: Auto, or a specific type when detection cannot identify it.</summary>
    public IListFeed<PcmTypeOption> PcmTypes =>
        ListFeed.Async(ct => this.GetPcmTypes(ct)).Selection(this.SelectedPcmTypeOption);

    public IState<PcmTypeOption> SelectedPcmTypeOption => State<PcmTypeOption>
        .Value(this, () => new PcmTypeOption(PcmType.Undefined));

    /// <summary>What detection found, for a label beside the write-type list.</summary>
    public IState<string> DetectionMessage => State<string>.Value(this, () => "Detecting PCM...");

    /// <summary>What the selected write type will do, so the user reads it before starting.</summary>
    public IState<string> WriteDescription => State<string>.Value(this, () => String.Empty);

    /// <summary>
    /// Whether to show the write-type list. A test write and a comparison are chosen from the menu and
    /// are not a choice on this page.
    /// </summary>
    public IState<bool> ShowWriteTypeList => State<bool>.Value(
        this, () => this.writeType != WriteType.TestWrite && this.writeType != WriteType.Compare);

    /// <summary>What the offered write types were, so Start can fall back sensibly.</summary>
    private OperationOptions? detectedOptions;

    public IState<bool> UseCustomKey => State<bool>.Value(this, () => false).ForEach((value, ct) => UseCustomKeyChanged(value, ct));
    public IState<bool> UseCustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<string> CustomKey => State<string>.Value(this, () => "");
    public IState<bool> CustomKeyEnabled => State<bool>.Value(this, () => true);
    private readonly UserLogBuffer _localUserMessages;

    public WriteModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter loggerAdapter,
        IPlatformService platformService,
        IPromptService promptService,
        IDispatcher dispatcher)
    {
        this.navigator = navigator;
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        this.promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        this.writeType = WriteModel.WriteType; // hacky workaround

        // Fire-and-forget init. The write-type selection is seeded by detection, not here.
        var _1 = this.Path.SetAsync(this.settingsService.GetLastWrittenFile());
        var _2 = this.UseCustomKey.SetAsync(this.settingsService.GetUseCustomKey());
        var _3 = this.CustomKey.SetAsync(this.settingsService.GetCustomKey());
        var _4 = this.EnableControls(false);
        _localUserMessages = new UserLogBuffer(text => this.UserLog.SetAsync(text).AsTask());
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

    private string GetCompletionText() => OperationOptions.DescribeCompletion(this.writeType);

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
                WriteTypeOption? selected = await this.SelectedWriteTypeOption.Value();

                // A clone-only PCM must not get a calibration write just because nothing was selected.
                if (selected == null
                    || selected.Value.Type == WriteType.None
                    || (this.detectedOptions != null && !this.detectedOptions.Offers(selected.Value.Type)))
                {
                    return this.detectedOptions?.PreferredWriteType(WritePlan.DefaultWriteType())
                        ?? WritePlan.DefaultWriteType();
                }

                return selected.Value.Type;
        }
    }

    /// <summary>The PCM type to force, or Undefined to let the manager identify it.</summary>
    private async Task<PcmType> GetForcedPcmType()
    {
        PcmTypeOption? selected = await this.SelectedPcmTypeOption.Value();
        return selected?.Type ?? PcmType.Undefined;
    }

    /// <summary>
    /// Detect the PCM and work out which write types it supports. A failure here is expected (nothing
    /// connected yet) and falls back to offering everything; the write still refuses what it cannot do.
    /// </summary>
    private async ValueTask<IImmutableList<WriteTypeOption>> GetWriteTypes(CancellationToken ct)
    {
        OSIDInfo? detected = null;
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Detecting PCM", true))
            {
                if (lease != null)
                {
                    DetectedModule? pcm = await lease.Vehicle.DetectAndSelectPcm(ct);
                    if (pcm != null)
                    {
                        detected = pcm.Info;
                        this.loggerAdapter.AddUserMessage($"Detected {detected.HardwareType} on {pcm.Bus}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            this.loggerAdapter.AddDebugMessage("WriteModel: unable to detect the PCM. " + exception.Message);
        }

        OperationOptions options = OperationOptions.For(detected);
        this.detectedOptions = options;
        await this.DetectionMessage.SetAsync(options.DetectionMessage, ct);

        // Preselect the user's standing preference when this PCM supports it; the policy decides.
        WriteType requested = this.settingsService.IsCalibrationWritePreferred()
            ? WriteType.Calibration
            : WritePlan.DefaultWriteType();
        WriteType preferred = options.PreferredWriteType(requested);
        await this.SelectedWriteTypeOption.SetAsync(new WriteTypeOption(preferred), ct);
        await this.WriteDescription.SetAsync(OperationOptions.DescribeWrite(preferred), ct);

        if (!options.CanWrite)
        {
            await this.StartEnabled.SetAsync(false, ct);
        }

        return ImmutableList.CreateRange(options.WriteTypes.Select(type => new WriteTypeOption(type)));
    }

    private ValueTask<IImmutableList<PcmTypeOption>> GetPcmTypes(CancellationToken ct)
    {
        List<PcmTypeOption> types = [new PcmTypeOption(PcmType.Undefined)];
        types.AddRange(OperationOptions.SelectablePcmTypes().Select(type => new PcmTypeOption(type)));
        return ValueTask.FromResult((IImmutableList<PcmTypeOption>)ImmutableList.CreateRange(types));
    }

    private async ValueTask SelectedWriteTypeChanged(WriteTypeOption? selected, CancellationToken ct)
    {
        if (selected == null || selected.Value.Type == WriteType.None)
        {
            return;
        }

        await this.WriteDescription.SetAsync(OperationOptions.DescribeWrite(selected.Value.Type), ct);

        // Remember the preference, as the old checkbox did.
        this.settingsService.ShouldPreferCalibrationWrite(selected.Value.Type == WriteType.Calibration);
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

        // The PCM-type override applies to every write; only the write-type list is hidden for a test
        // write or comparison (see ShowWriteTypeList).
        await this.OptionsEnabled.SetAsync(!busy);
    }

    [Command]
    public async ValueTask Start(CancellationToken cancellationToken)
    {
#if ANDROID
        if (!await Platforms.Android.PermissionMethods.ExtractKernelsToFileAndroid())
        {
            // Say so here; otherwise this surfaces later as an unexplained missing-kernel error.
            await this.AddUserMessage("Storage access was not granted, so the kernels could not be installed. Grant it and try again.");
            await this.EnableControls(false);
            return;
        }
#endif
        await this.EnableControls(true);
        string? path = string.Empty;
#if !ANDROID // Force Android devices to use the file picker to load bytes from file. StorageFile.Path doesn't seem to play freindly as means to open the file again.
        path = await this.Path.Value();
#endif
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

                PcmType forcedPcmType = await this.GetForcedPcmType();
                if (forcedPcmType != PcmType.Undefined)
                {
                    await this.AddUserMessage("Forcing PCM type: " + forcedPcmType);
                }

#if WINDOWS
                using (new AwayMode())
                {
                    await PerformWrite(path, writeManager, forcedPcmType);
                }
#elif ANDROID
                await PerformWrite(path, writeManager, forcedPcmType);
#endif
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage("Write failed: ");
            await this.AddUserMessage(exception.Message);
            await Task.Delay(1000, cancellationToken);
            await this.AddDebugMessage(exception.ToString());
            await this.EnableControls(false);
        }
        finally
        {
            // The buffer publishes on a timer, so the closing lines need an explicit flush.
            await _localUserMessages.FlushNow();
            this.tokenSource = null;
            await this.EnableControls(false);
        }
    }

    private async Task PerformWrite(string path, WriteManager writeManager, PcmType forcedPcmType)
    {
#if ANDROID
        // A StorageFile path is not reliably re-openable, so the package is parsed from the bytes read
        // at pick time. The raw-image overload cannot carry the slave references a .phz holds.
        if (_isPackageFile)
        {
            PcmPackage package;
            try
            {
                using (MemoryStream stream = new MemoryStream(_fileBuffer, writable: false))
                {
                    package = PackageStore.Load(stream, path);
                }
            }
            catch (PackageException exception)
            {
                await this.AddUserMessage("Unable to load file: " + exception.Message);
                return;
            }

            if (await writeManager.Write(package, forcedPcmType))
            {
                await this.AddUserMessage(this.GetCompletionText());
            }

            return;
        }

        await writeManager.Write(_fileBuffer, forcedPcmType);
#else
        if (await writeManager.Write(path, forcedPcmType))
        {
            await this.AddUserMessage(this.GetCompletionText());
            this.tokenSource = null;
            await this.EnableControls(false);
        }
#endif
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
        this.platformService.PrepareChildWindow(openPicker);
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".phz");
        openPicker.FileTypeFilter.Add(".bin");
        StorageFile file = await openPicker.PickSingleFileAsync();
        if (file == null)
        {
            return null;
        }

        _isPackageFile = System.IO.Path.GetExtension(file.Name)
            .Equals(".phz", StringComparison.OrdinalIgnoreCase);
#if ANDROID
        var openedFile = await file.OpenReadAsync();
        _fileBuffer = openedFile.AsStream().ToMemoryStream().ToArray();
        openedFile.Dispose();
        return file.Name;
#else
        _fileBuffer = Array.Empty<byte>();
        return file.Path;
#endif
    }

    private Task Alert(string message, string title) => this.promptService.Alert(message, title);

    private Task<bool> PromptForYesNo(string message, string title) => this.promptService.AskYesNo(message, title);

    public async Task AddUserMessage(string message)
    {
        _localUserMessages.Append(message);
        await Task.CompletedTask;
    }

    public async Task AddDebugMessage(string message)
    {
        // TODO: Debug message logging
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
