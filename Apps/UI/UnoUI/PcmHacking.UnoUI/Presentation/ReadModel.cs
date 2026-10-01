// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System.Globalization;
using System.Runtime.CompilerServices;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Pickers;

namespace PcmHacking.UnoUI.Presentation;

public partial record ReadModel : IAsyncLogger
{
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IPlatformService platformService;
    private readonly IPromptService promptService;
    private readonly IDispatcher dispatcher;
    private StorageFile _selectedFile = null!;

    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Read PCM"; } }

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

    public IState<bool> UseCustomKey => State<bool>.Value(this, () => false).ForEach((value, ct) => UseCustomKeyChanged(value, ct));
    public IState<bool> UseCustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<string> CustomKey => State<string>.Value(this, () => "");
    public IState<bool> CustomKeyEnabled => State<bool>.Value(this, () => true);

    /// <summary>Whether the PCM-type choice can be changed (not while a read runs).</summary>
    public IState<bool> OptionsEnabled => State<bool>.Value(this, () => true);

    /// <summary>
    /// The PCM type override: Auto, or a specific type. Forcing a type skips the operating-system query,
    /// which is how a PCM that will not report its OSID can still be read.
    /// </summary>
    public IListFeed<PcmTypeOption> PcmTypes =>
        ListFeed.Async(ct => this.GetPcmTypes(ct)).Selection(this.SelectedPcmTypeOption);

    public IState<PcmTypeOption> SelectedPcmTypeOption => State<PcmTypeOption>
        .Value(this, () => new PcmTypeOption(PcmType.Undefined));
    private readonly UserLogBuffer _localUserMessages;

    public ReadModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter loggerAdapter,
        IPlatformService platformService,
        IPromptService promptService,
        IDispatcher dispatcher)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        this.promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        
        var _1 = this.UseCustomKey.SetAsync(this.settingsService.GetUseCustomKey());
        var _2 = this.CustomKey.SetAsync(this.settingsService.GetCustomKey());
        var _3 = this.EnableControls(false);
        _localUserMessages = new UserLogBuffer(text => this.UserLog.SetAsync(text).AsTask());
    }

    private ValueTask<IImmutableList<PcmTypeOption>> GetPcmTypes(CancellationToken ct)
    {
        List<PcmTypeOption> types = [new PcmTypeOption(PcmType.Undefined)];
        types.AddRange(OperationOptions.SelectablePcmTypes().Select(type => new PcmTypeOption(type)));
        return ValueTask.FromResult((IImmutableList<PcmTypeOption>)ImmutableList.CreateRange(types));
    }

    /// <summary>The PCM type to force, or Undefined to let the read manager identify it.</summary>
    private async Task<PcmType> GetForcedPcmType()
    {
        PcmTypeOption? selected = await this.SelectedPcmTypeOption.Value();
        return selected?.Type ?? PcmType.Undefined;
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
        await this.OptionsEnabled.SetAsync(!busy);

        await this.CancelEnabled.SetAsync(busy);
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
#if !ANDROID
        path = await this.Path.Value();
#endif
        if (string.IsNullOrWhiteSpace(path) || string.Compare(path, defaultPath, StringComparison.OrdinalIgnoreCase) == 0)
        {
            path = await this.PromptForFileSavePath();
            if (string.IsNullOrWhiteSpace(path) || string.Compare(path, defaultPath, StringComparison.OrdinalIgnoreCase) == 0)
            {
                await this.AddUserMessage("No file selected.");
                await this.StartEnabled.SetAsync(true);
                await this.CancelEnabled.SetAsync(false);
                return;
            }
            await this.Path.SetAsync(path);
        }

        // TODO: Review the scenarios in which the given cancellationToken
        // can get signaled, and review how the read process handles those
        // signals.
        this.tokenSource = new CancellationTokenSource();
        CancellationToken readCancellationToken = this.tokenSource.Token;
        try
        {
            ConnectionLease lease = await this.connectionService.BeginActivity("Reading PCM", false);
            using (new LogInterceptor(this.loggerAdapter, this))
            {
                // I suspect a bug in the Uno Platform's ContentDialog implementation, hence the static object in the 'if' statement.
                // See notes in WriteModel for details.
                await this.navigator.GetDataAsync<DelayModel, DelayResult>(this, cancellation: cancellationToken);
                if (DelayModel.Result?.Proceed == false)
                {
                    await this.AddUserMessage("Read aborted.");
                    return;
                }

                lease.Vehicle.Enable4xReadWrite = this.settingsService.Is4xReadWriteEnabled();

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

                ReadManager readManager = new(
                    this.loggerAdapter,
                    lease.Vehicle,
                    this.Invoke,
                    this.PromptForFileSavePath,
                    this.PromptForPcmType,
                    this.Alert,
                    this.PromptForYesNo,
                    readCancellationToken);

                PcmType forcedPcmType = await this.GetForcedPcmType();
                if (forcedPcmType != PcmType.Undefined)
                {
                    await this.AddUserMessage("Forcing PCM type: " + forcedPcmType);
                }
#if WINDOWS
                using (new AwayMode())
                {
                    await performRead(path, lease, readManager, forcedPcmType);
                    lease.Dispose();
                }
#elif ANDROID
                Progress<ProgressUpdate> progress = new Progress<ProgressUpdate>((progress) => {
                    _ = UpdateProgress(progress);
                });

                Platforms.Android.DataService.StartService("Read PCM", performRead(path, lease, readManager, forcedPcmType, progress),
                    async () =>
                    {
                        if (readCancellationToken.IsCancellationRequested)
                        {
                            await this.AddUserMessage("Read was canceled.");
                        }
                        else
                        {
                            await this.AddUserMessage("Read completed successfully.");
                        }
                        this.tokenSource = null;
                        await this.EnableControls(false);
                        lease.Dispose();
                    },
                    async () =>
                    {
                        await this.AddUserMessage("Read failed: ");
                        this.tokenSource = null;
                        await this.EnableControls(false);
                        lease.Dispose();
                    });
                return;
#endif
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage("Read failed: ");
            await this.AddUserMessage(exception.Message);
            await this.AddDebugMessage(exception.ToString());
        }
        finally
        {
            // The buffer publishes on a timer, so the closing lines need an explicit flush.
            await _localUserMessages.FlushNow();
#if !ANDROID
            this.tokenSource = null;
            await this.EnableControls(false);
#endif
        }
    }

    private async Task performRead(
        string path,
        ConnectionLease lease,
        ReadManager readManager,
        PcmType forcedPcmType,
        IProgress<ProgressUpdate>? progress = null)
    {
        // Save as a .phz package (master image + any slave references) when the chosen file is .phz;
        // otherwise write the raw master image, as before.
        bool asPackage = _selectedFile != null
            && System.IO.Path.GetExtension(_selectedFile.Name).Equals(".phz", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (asPackage)
            {
                PcmPackage? package = await readManager.ReadToPackage(progress, forcedPcmType);
                if (_selectedFile != null && package != null)
                {
                    using (Stream writeStream = await _selectedFile.OpenStreamForWriteAsync())
                    {
                        // Opening for write does not truncate, so saving over a larger existing file
                        // would leave its tail behind and produce a corrupt, oversized image.
                        writeStream.SetLength(0);
                        PackageStore.Save(writeStream, package, _selectedFile.Name);
                    }
                }
            }
            else
            {
                Stream? readContents = await readManager.Read(progress, forcedPcmType);
                if (_selectedFile != null && readContents != null)
                {
                    using (Stream writeStream = await _selectedFile.OpenStreamForWriteAsync())
                    {
                        writeStream.SetLength(0);
                        if (readContents.CanSeek)
                        {
                            readContents.Position = 0;
                        }

                        await readContents.CopyToAsync(writeStream);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage(exception.Message);
            await this.AddDebugMessage(exception.ToString());
            throw;
        }
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        await this.AddUserMessage("Cancelling.");
        this.tokenSource?.Cancel();
        this.tokenSource = null;
    }

    [Command]
    public async Task ChooseFile()
    {
        try
        {
            await this.StartEnabled.SetAsync(false);
            string? path = await this.PromptForFileSavePath();
            await this.Path.SetAsync(path);
        }
        finally
        {
            await this.StartEnabled.SetAsync(true);
        }
    }

    private async Task UpdateProgress(ProgressUpdate progress)
    {
#if ANDROID
        if (Platforms.Android.DataService.IsServiceRunning())
        {
            int fixedPercentage = (int)(progress.Percentage * 100);
            Platforms.Android.DataService.UpdateProgress(fixedPercentage, $"Reading {progress.PayloadLength} bytes from 0x{progress.Address:X6}");
        }

#endif
        await Invoke(async () =>
        {
            await this.StatusUpdateActivity($"Reading {progress.PayloadLength} bytes from 0x{progress.Address:X6}");
            await this.StatusUpdateTimeRemaining($"T-{progress.TimeRemaining}");
            await this.StatusUpdatePercentDone($"{(progress.Percentage * 100.0):0.00}%");
            await this.StatusUpdateRetryCount(progress.RetryCount.ToString());
            await this.StatusUpdateProgressBar(progress.Percentage, true);
            await this.StatusUpdateKbps($"{progress.Rate} Kbps");
        });
    }

    private async Task Invoke(Action action)
    {
        var tcs = new TaskCompletionSource();
        await this.dispatcher.ExecuteAsync(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        await tcs.Task;
    }

    private async Task<string?> PromptForFileSavePath()
    {
        // Open a Save-As dialog to get the file path
        FileSavePicker savePicker = new FileSavePicker();
        this.platformService.PrepareChildWindow(savePicker);
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("Binary", new List<string>() { ".bin" });
        savePicker.FileTypeChoices.Add("PcmHammer package", new List<string>() { ".phz" });
        // Same naming rule as the other UIs. This picker runs before the read, so there is no package
        // to take a module type and OSID from yet and the name is the date-stamped fallback; the rule
        // still lives in the library rather than being spelled out again here.
        savePicker.SuggestedFileName = PackageStore.DefaultBaseName(null, null, null) + ".bin";
        StorageFile file = await savePicker.PickSaveFileAsync();
        if (file == null)
        {
            return null; // TODO: change the return-type to Task<string?> in the refactoring branch.
        }
        _selectedFile = file;
        return file.Name;
    }

    private Task<PcmType> PromptForPcmType() => this.promptService.AskPcmType();

    private Task<bool> PromptForYesNo(string message, string title) => this.promptService.AskYesNo(message, title);

    private Task Alert(string message, string title) => this.promptService.Alert(message, title);


    public async Task AddUserMessage(string message)
    {
        _localUserMessages.Append(message);
        await Task.CompletedTask;
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
