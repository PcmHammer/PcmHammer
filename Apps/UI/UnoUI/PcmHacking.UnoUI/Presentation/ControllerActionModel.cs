using Microsoft.UI.Dispatching;
using PcmHacking.ECU;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Timers;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Pickers;
using Windows.UI.Core;

namespace PcmHacking.UnoUI.Presentation;

public record ControllerActionResult(bool Suceeded = true);

public partial record ControllerActionModel : IAsyncLogger
{
    public ControllerActionResult Result;
    private readonly ECUActionArguments _actionArguments;
    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IDispatcher dispatcher;
    private StorageFile? _selectedFile;
    private ControllerPageObjects pageObjects;
    private readonly string actionText;
    private System.Timers.Timer _logUpdateTimer;
    private List<string> _localMessages = [];
    private int _logTimerDelay = 100;

    private CancellationTokenSource? tokenSource;

    public string Title { get { return "Read PCM"; } }

    public IState<string> CancelText => State<string>.Value(this, ()=> "Cancel");

    public IListState<string> UserLog => ListState<string>.Empty(this);
    public IState<string> Activity => State<string>.Value(this, () => String.Empty);
    public IState<string> TimeRemaining => State<string>.Value(this, () => String.Empty);
    public IState<string> PercentDone => State<string>.Value(this, () => String.Empty);
    public IState<string> RetryCount => State<string>.Value(this, () => String.Empty);
    public IState<string> Kbps => State<string>.Value(this, () => String.Empty);
    public IState<double> Progress => State<double>.Value(this, () => 0.0);
    private bool _isActive = false;

    public ControllerActionModel(
        INavigator navigator,
        IConnectionService connectionService,
        LoggerAdapter loggerAdapter,
        IDispatcher dispatcher,
        ECUActionArguments arguments)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _actionArguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
#if ANDROID
        _logTimerDelay = 1000;
#endif
        _logUpdateTimer = new(_logTimerDelay);
        _logUpdateTimer.Elapsed += _logUpdateTimer_Elapsed;

        pageObjects = new ControllerPageObjects
        {
            PromptYesOrNo = async (t, m) => await PromptForYesNo(t, m),
            Invoke = async (action) => await Invoke(action),
            ShowAlert = async (t, m) => await this.Alert(t, m),
            PromptForHardwareType = null,
        };
        this.actionText = $"{(_actionArguments.SelectedAction == ControllerActions.Write ? $"{_actionArguments.WriteType} " : "")}{_actionArguments.SelectedAction}";
        _ = Start();
        _logUpdateTimer.Start();
    }

    private void _logUpdateTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        UserLog.UpdateAsync(updater: e => e = _localMessages.ToImmutableList());
    }

    private async Task<bool> ControllerPreFlightChecks(ECUBase pcmInfo)
    {
        if (!pcmInfo.IsSupported && _actionArguments.HardwareType != PcmType.Undefined)
        {
            this.loggerAdapter.AddUserMessage("Detected hardware type override on Unsupported ECU. Please be sure to post results!");
            pcmInfo = ECUFactory.GetControllerOverride(_actionArguments.HardwareType, pcmInfo.GetCurrentOSID());
            this.loggerAdapter.AddUserMessage($"Continuing read with hardware type of {_actionArguments.HardwareType}");
        }
        // Pre flight checks to block invalid write operations by PCM type.
        if (!pcmInfo.IsSupported)
        {
            string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported.";
            await new AlertPrompt("Abort", msg).ShowAsync();
            return false;
        }

        if (!pcmInfo.IsSupportedRead)
        {
            string msg = $"Abort: The connected {pcmInfo.HardwareType.ToString()} PCM is not supported for read operations.";
            await new AlertPrompt("Abort", msg).ShowAsync();
            return false;
        }

        if (pcmInfo.IsUnderDevelopment)
        {
            string msg = $"WARNING: {pcmInfo.HardwareType.ToString()} Support is still in development.";
            ContentDialogResult result = await new BinaryPrompt("Do you wish to continue?", msg).ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return false;
            }
        }

        // Write mode only checks.
        if (_actionArguments.SelectedAction == ControllerActions.Write)
        {

            // If the factory binary is not paritioned we cant write by segment, block the non-full write types
            if (!pcmInfo.IsSupportedWriteBySegment && (_actionArguments.WriteType == WriteType.Calibration || _actionArguments.WriteType == WriteType.OsPlusCalibrationPlusBoot || _actionArguments.WriteType == WriteType.Parameters))
            {
                string msg = $"Error: The connected {pcmInfo.HardwareType.ToString()} PCM binary format is not partitioned and does not support partial write." + Environment.NewLine +
                            "You will need to do a Write Full Flash (Clone) instead.";
                await new AlertPrompt(msg, "Error").ShowAsync();
                return false;
            }

            // If we cant write the slave, warn the user of operating system changes
            if (pcmInfo.HardwareSlaveCPU == true && !pcmInfo.IsSupportedWriteSlaveCPU && (_actionArguments.WriteType == WriteType.Full || _actionArguments.WriteType == WriteType.OsPlusCalibrationPlusBoot))
            {
                string msg = $"Warning: Writes to the {pcmInfo.HardwareType.ToString()} slave CPU are not supported." + Environment.NewLine +
                            "You must have another way to update the slave CPU to match when you change operating system, else electroncic throttle may not work." + Environment.NewLine +
                            "Restore this PCM to its original operating system if this happens." + Environment.NewLine +
                            "Do you want to continue?";
                
                ContentDialogResult result = await new BinaryPrompt("Warning!", msg).ShowAsync();

                if (result != ContentDialogResult.Primary)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public async ValueTask Start()
    {
        _isActive = true;
        await this.AddUserMessage($"Beginning selected {this.actionText} operation.");

#if ANDROID
        if (!await Platforms.Android.PermissionMethods.IsStorageGranted())
        {
            bool result = await DialogService.ShowBinaryPrompt("File permissions",
        "PCM Hammer needs access to file storage\r\n" +
        "to load the required kernel bin. Press\r\n" +
        "Okay to proceed to grant this permission.", primarySelection: PrimaryButton.Left);

            if (result)
            {
                await dispatcher.ExecuteAsync(async (ct) => await Platforms.Android.PermissionMethods.GrantStoragePermissions());
            }
        }
        await Platforms.Android.PermissionMethods.ExtractKernelsToFileAndroid();
#endif

        // TODO: Review the scenarios in which the given cancellationToken
        // can get signaled, and review how the read process handles those
        // signals.
        this.tokenSource = new CancellationTokenSource();
        CancellationToken actionCancellationToken = this.tokenSource.Token;
        try
        {
            ConnectionLease lease = await this.connectionService.BeginActivity($"{this.actionText} PCM", false);
            LogInterceptor interceptor = new LogInterceptor(this.loggerAdapter, this);

            lease.Vehicle.Enable4xReadWrite = _actionArguments?.UseHighSpeed ?? false;
            lease.Vehicle.UserDefinedKey = (_actionArguments?.CustomKey ?? 0) == 0 ? -1 : (int)(_actionArguments?.CustomKey ?? 0);

            if (this.connectionService.GetConnectedECU() != null)
            {
                bool checksRequired = await dispatcher.ExecuteAsync<bool>(async (ct) =>
                {
                    return await ControllerPreFlightChecks(this.connectionService.GetConnectedECU());
                }, actionCancellationToken);
                _actionArguments.PreFlightChecksRequired = checksRequired;
            }

            Progress<ProgressUpdate>? progress = new Progress<ProgressUpdate>((progress) =>
            {
                _ = UpdateProgress(progress);
            });

            ControllerManager manager = new(lease.Vehicle,
                _actionArguments ?? new(),
                pageObjects,
                actionCancellationToken,
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
            if(!await Platforms.Android.PermissionMethods.IsNotificationsGranted())
            {
                bool result = await DialogService.ShowBinaryPrompt("Grant permission?",
                    "PCM Hammer uses a background service with push\r\n" +
                    "notifications. Press Okay to grant this permission.", primarySelection: PrimaryButton.Left);
                if (result)
                {
                    if(await dispatcher.ExecuteAsync(async (ct) => await Platforms.Android.PermissionMethods.GrantNotificationPermission()))
                    {
                        // TODO: What do we do when users perform confusing actions?
                    }
                }
                else
                {
                    // TODO: Create a user variable in settings to store the wish for no notifications. Use this flag to skip the update to the notification down flow.
                }
            }
            Platforms.Android.DataService.StartService(this.actionText, PerformControllerAction(manager),
                async () =>
                {
                    if (actionCancellationToken.IsCancellationRequested)
                    {
                        await this.AddUserMessage($"{this.actionText} was canceled.");
                    }
                    this.tokenSource = null;
                    lease.Dispose();
                    interceptor.Dispose();
                    _isActive = false;
                    await this.CancelText.SetAsync("Close");
                },
                async () =>
                {
                    await this.AddUserMessage($"{this.actionText} failed: ");
                    this.tokenSource = null;
                    lease.Dispose();
                    interceptor.Dispose();
                    _isActive = false;
                    await this.CancelText.SetAsync("Close");
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
            _isActive = false;
            await this.CancelText.SetAsync("Close");
#endif
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
                        writeStream.SetLength(manager.ActionArgs.ContentStream.Length);
                        writeStream.Position = 0;
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
            Result = new(success);
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
        else
        {
            await this.navigator.GoBack(this);
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
            await this.StatusUpdateActivity(progress.Activity);
            await this.StatusUpdateTimeRemaining($"T-{progress.TimeRemaining}");
            await this.StatusUpdatePercentDone($"{(progress.Percentage * 100.0):0.00}%");
            await this.StatusUpdateRetryCount(progress.RetryCount.ToString());
            await this.StatusUpdateProgressBar(progress.Percentage, progress.ProgressBarVisible);
            await this.StatusUpdateKbps($"{progress.Rate} Kbps");
        });
    }

    private async Task Invoke(Action? action)
    {
        action?.Invoke();
    }

    private async Task<bool> PromptForYesNo(string message, string title)
    {
        return true;
    }

    private async Task Alert(string message, string title)
    {

    }

    public async Task AddUserMessage(string message, LogLevels level = 0)
    {
        _localMessages.Insert(0, message);
    }

    public async Task AddDebugMessage(string message)
    {
        if (_actionArguments?.ShowDebug ?? false)
        {
            _localMessages.Insert(0, message);
        }
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
