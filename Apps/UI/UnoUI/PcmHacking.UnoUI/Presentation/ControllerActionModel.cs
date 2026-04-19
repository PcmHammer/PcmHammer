using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System.Globalization;
using System.Runtime.CompilerServices;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Pickers;
using Windows.UI.Core;

namespace PcmHacking.UnoUI.Presentation;

public partial record ControllerActionModel : IAsyncLogger
{
    public static ECUActionArguments? ECUActionArguments = null;

    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IPlatformService platformService;
    private readonly IDispatcher dispatcher;
    private StorageFile? _selectedFile;
    private ControllerPageObjects pageObjects;
    private readonly string actionText;

    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Read PCM"; } }

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);

    public IState<string> Path => State<string>.Value(this, () => defaultPath); 
    public IListState<string> UserLog => ListState<string>.Empty(this);
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
    public IState<string> CancelText => State<string>.Value(this, ()=> "Cancel");
    private List<string> _localUserMessages;
    private bool _isActive = false;

    public ControllerActionModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter loggerAdapter,
        IPlatformService platformService,
        IDispatcher dispatcher)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        
        _ = this.UseCustomKey.SetAsync(this.settingsService.GetUseCustomKey());
        _ = this.CustomKey.SetAsync(this.settingsService.GetCustomKey());
        _ = this.EnableControls(false);
        _localUserMessages = [];
        pageObjects = new ControllerPageObjects
        {
            PromptYesOrNo = this.PromptForYesNo,
            Invoke = this.Invoke,
            ShowAlert = this.Alert,
            PromptForHardwareType = null,
        };
        this.actionText = $"{(ECUActionArguments.SelectedAction == ControllerActions.Write ? $"{ECUActionArguments.WriteType} " : "")}{ECUActionArguments.SelectedAction}";
        if(ECUActionArguments != null)
        {
            _ = Start();
        }
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
    }

    public async ValueTask Start()
    {
        _isActive = true;
        await this.AddUserMessage($"Beginning selected {this.actionText} operation.");
#if ANDROID
        await Platforms.Android.PermissionMethods.ExtractKernelsToFileAndroid();
#endif
        await this.EnableControls(true);

        // TODO: Review the scenarios in which the given cancellationToken
        // can get signaled, and review how the read process handles those
        // signals.
        this.tokenSource = new CancellationTokenSource();
        CancellationToken readCancellationToken = this.tokenSource.Token;
        try
        {
            ConnectionLease lease = await this.connectionService.BeginActivity($"{this.actionText} PCM", false);
            LogInterceptor interceptor = new LogInterceptor(this.loggerAdapter, this);

            lease.Vehicle.Enable4xReadWrite = ECUActionArguments?.UseHighSpeed ?? false;
            lease.Vehicle.UserDefinedKey = (ECUActionArguments?.CustomKey ?? 0) == 0 ? -1 : (int)(ECUActionArguments?.CustomKey ?? 0);

            Progress<ProgressUpdate>? progress = new Progress<ProgressUpdate>((progress) =>
            {
                _ = UpdateProgress(progress);
            });

            ControllerManager manager = new(lease.Vehicle,
                ECUActionArguments ?? new(),
                pageObjects,
                readCancellationToken,
                progress,
                this.loggerAdapter);

            manager.Initialize();
#if WINDOWS
                using (new AwayMode())
                {
                    await PerformControllerAction(manager);
                    lease.Dispose();
                    interceptor.Dispose();
                }
#elif ANDROID
            Platforms.Android.DataService.StartService(this.actionText, PerformControllerAction(manager),
                async () =>
                {
                    if (readCancellationToken.IsCancellationRequested)
                    {
                        await this.AddUserMessage($"{this.actionText} was canceled.");
                    }
                    else
                    {
                        await this.AddUserMessage($"{this.actionText} completed successfully.");
                    }
                    this.tokenSource = null;
                    await this.EnableControls(false);
                    lease.Dispose();
                    interceptor.Dispose();
                },
                async () =>
                {
                    await this.AddUserMessage($"{this.actionText} failed: ");
                    this.tokenSource = null;
                    await this.EnableControls(false);
                    lease.Dispose();
                    interceptor.Dispose();
                });
            return;
#endif
        }
        catch (Exception exception)
        {
            await this.AddUserMessage($"{this.actionText} failed: ");
            await this.AddUserMessage(exception.Message);
            await this.AddDebugMessage(exception.ToString());
        }
        finally
        {
#if !ANDROID
            this.tokenSource = null;
            await this.EnableControls(false);
#endif
            ECUActionArguments = null;
            _isActive = false;
            await this.CancelText.SetAsync("Close");
        }
    }

    private async Task PerformControllerAction(ControllerManager manager)
    {
        bool success = false;
        try
        {
            if(manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            _selectedFile = manager.ActionArgs.StorageFileObject as StorageFile;
            if (_selectedFile == null)
            {
                throw new NullReferenceException(nameof(_selectedFile));
            }
            if (manager.ActionArgs.SelectedAction == ControllerActions.Undefined)
            {
                throw new InvalidOperationException("ControllerManager was not properly initialized with a valid action.");
            }
            switch(manager.ActionArgs.SelectedAction)
            {
                case ControllerActions.Read:
                    if (_selectedFile != null && await manager.BeginAction() && manager.ActionArgs.ContentStream != null)
                    {
                        Stream writeStream = await _selectedFile.OpenStreamForWriteAsync();
                        manager.ActionArgs.ContentStream.Position = 0;
                        await manager.ActionArgs.ContentStream.CopyToAsync(writeStream);
                        await writeStream.FlushAsync();
                        await writeStream.DisposeAsync();
                        success = true;
                    }
                    return;
                case ControllerActions.Write:
                    if (_selectedFile != null)
                    {
                        manager.ActionArgs.ContentStream = new MemoryStream();
                        Stream? content = await _selectedFile.OpenStreamForReadAsync();
                        await content.CopyToAsync(manager.ActionArgs.ContentStream);
                        if (await manager.BeginAction())
                        {
                            success = true;
                        }
                        manager.ActionArgs.ContentStream.Dispose();
                    }
                    return;
            }
        }
        catch (Exception exception)
        {
            await this.AddUserMessage(exception.Message);
            await this.AddDebugMessage(exception.ToString());
            throw;
        }
        finally
        {
            string suffix = success == true ? "successfully." : "with errors.";
            await this.AddUserMessage($"{this.actionText} completed {suffix}");
        }
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        if (_isActive)
        {
            await this.AddUserMessage("Cancelling.");
            this.tokenSource?.Cancel();
            this.tokenSource = null;
        }
    }

    private async Task UpdateProgress(ProgressUpdate progress)
    {
        if (progress.UserMessage != null)
        {
            await AddUserMessage(progress.UserMessage);
            return;
        }
        if (progress.DebugMessage != null)
        {
            await AddDebugMessage(progress.DebugMessage);
            return;
        }
#if ANDROID
        if (Platforms.Android.DataService.IsServiceRunning())
        {
            int fixedPercentage = (int)(progress.Percentage * 100);
            Platforms.Android.DataService.UpdateProgress(fixedPercentage, $"Reading {progress.PayloadLength} bytes from 0x{progress.Address:X6}");
        }
#endif
        await Invoke(async () =>
        {
            await this.StatusUpdateActivity(progress.Activity);
            await this.StatusUpdateTimeRemaining($"T-{progress.TimeRemaining}");
            await this.StatusUpdatePercentDone($"{(progress.Percentage * 100.0):0.00}%");
            await this.StatusUpdateRetryCount(progress.RetryCount.ToString());
            await this.StatusUpdateProgressBar(progress.Percentage, progress.ProgressBarVisible);
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
    
    public async Task AddUserMessage(string message)
    {

        await this.UserLog.Update(updater: existing => existing.Add(message), ct: CancellationToken.None);
    }
    
    public async Task AddDebugMessage(string message)
    {

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
