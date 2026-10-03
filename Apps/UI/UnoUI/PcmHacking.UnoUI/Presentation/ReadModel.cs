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

        // Both are hoisted out of the try so that a failure anywhere below still releases them.
        // The lease holds the connection's state-change semaphore and leaves the connection in the
        // Active state, which disables the back button, so dropping one strands the user on this
        // page with a disabled Start and no way out.
        ConnectionLease? lease = null;
        LogInterceptor? interceptor = null;
        bool handedOffToService = false;
        try
        {
            lease = await this.connectionService.BeginActivity("Reading PCM", false);
            interceptor = new LogInterceptor(this.loggerAdapter, this);

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
            }
#elif ANDROID
            Progress<ProgressUpdate> progress = new Progress<ProgressUpdate>((progress) => {
                _ = UpdateProgress(progress);
            });

            // The read runs in a foreground service after this method returns, so the lease and
            // the log interceptor outlive the method and are released by the callbacks instead.
            ConnectionLease startedLease = lease;
            LogInterceptor startedInterceptor = interceptor;
            Task readTask = performRead(path, lease, readManager, forcedPcmType, progress);

            Platforms.Android.DataService.StartService("Read PCM", readTask,
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

                    await this.FinishRead(startedLease, startedInterceptor);
                },
                async () =>
                {
                    // Report what actually failed. This used to log "Read failed: " and nothing else,
                    // because the exception stayed on the task and was never looked at.
                    Exception? failure = readTask.Exception?.GetBaseException();
                    await this.AddUserMessage("Read failed: " + (failure?.Message ?? "unknown error"));
                    this.loggerAdapter.AddDebugMessage(failure?.ToString() ?? "The read task faulted with no exception.");
                    await this.FinishRead(startedLease, startedInterceptor);
                });

            handedOffToService = true;
            return;
#endif
        }
        catch (Exception exception)
        {
            await this.AddUserMessage("Read failed: " + exception.Message);
            this.loggerAdapter.AddDebugMessage(exception.ToString());
        }
        finally
        {
            if (!handedOffToService)
            {
                await this.FinishRead(lease, interceptor);
            }

            // The buffer publishes on a timer, so the closing lines need an explicit flush.
            await _localUserMessages.FlushNow();
        }
    }

    /// <summary>
    /// Release the connection and re-enable the controls. On Android this runs from the service
    /// callback, long after Start has returned.
    /// </summary>
    private async Task FinishRead(ConnectionLease? lease, IDisposable? interceptor)
    {
        this.tokenSource = null;

        try
        {
            await this.EnableControls(false);
        }
        finally
        {
            // Last, and unconditionally: the lease is what re-enables the back button.
            interceptor?.Dispose();
            lease?.Dispose();
        }

        await _localUserMessages.FlushNow();
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
            this.loggerAdapter.AddDebugMessage(exception.ToString());
            throw;
        }
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        // Keep the source. Clearing it here meant the first click cancelled and every later click
        // only logged "Cancelling.", which looked identical to a cancel that was being ignored.
        CancellationTokenSource? source = this.tokenSource;
        if (source == null)
        {
            await this.AddUserMessage("Nothing to cancel.");
        }
        else if (source.IsCancellationRequested)
        {
            await this.AddUserMessage("Already cancelling; waiting for the current block to finish.");
        }
        else
        {
            await this.AddUserMessage("Cancelling.");
            source.Cancel();
        }

        await _localUserMessages.FlushNow();
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

    /// <summary>
    /// Update Android's foreground-service notification.
    /// </summary>
    /// <remarks>
    /// Deliberately does not touch the on-screen fields. KernelReader.ReportProgress already sends
    /// those through the logger, which the log interceptor routes to the StatusUpdate methods below,
    /// so updating them here as well had two writers putting differently formatted versions of the
    /// same values into the same states - the display flickered between "42%" and "42.00%", two
    /// spellings of the activity, and two different scalings of the transfer rate. The logger path is
    /// the one to keep: it is the only one the Windows head has, since that path passes no IProgress.
    /// </remarks>
    private Task UpdateProgress(ProgressUpdate progress)
    {
#if ANDROID
        if (Platforms.Android.DataService.IsServiceRunning())
        {
            int fixedPercentage = (int)(progress.Percentage * 100);

            // Address already carries its "0x" prefix, and it is a string, so the X6 format this
            // used to apply did nothing except produce "0x0x002B000".
            Platforms.Android.DataService.UpdateProgress(
                fixedPercentage,
                $"Reading {progress.PayloadLength} bytes from {progress.Address}");
        }
#endif

        return Task.CompletedTask;
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
        // Nothing to do: this is the interceptor handing back a message the adapter has already put
        // in the log buffer, and the Read page shows user messages only. Writing it to the buffer
        // here would store every library debug line twice. Code in this class that wants something
        // in the debug log calls loggerAdapter.AddDebugMessage directly.
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
        await this.RetryCount.SetAsync("Retries: " + retries);
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
