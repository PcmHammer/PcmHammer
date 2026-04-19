using Microsoft.Extensions.Logging;
using PcmHacking.ECU;
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

public partial record ControllerActionSetupModel : IAsyncLogger
{
    public static ControllerActions SelectedAction = ControllerActions.Write;

    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IPlatformService platformService;
    private readonly IDispatcher dispatcher;

    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Write PCM"; } }

    public IListFeed<string> HardwareTypes => ListFeed<string>.Async(ct => this.GetHardwareTypes(ct)).Selection(SelectedHardwareType);
    public IState<string> SelectedHardwareType => State<string>.Value(this, () => this.GetCurrentHardwareType().Result);

    public IListFeed<string> WriteTypes => ListFeed<string>.Async(ct => this.GetWriteTypes(ct)).Selection(SelectedWriteType);
    public IState<string> SelectedWriteType => State<string>.Value(this, () => { return Enum.GetName(WriteType.Test) ?? "Test"; });

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

    public IState<bool> UseCustomKey => State<bool>.Value(this, () => false).ForEach((value, ct) => UseCustomKeyChanged(value, ct));
    public IState<bool> UseCustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<string> CustomKey => State<string>.Value(this, () => "");
    public IState<bool> CustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<bool> UseHighSpeed => State<bool>.Value(this, () => this.settingsService.Is4xReadWriteEnabled());
    public IState<bool> IsHardwareSelectable => State<bool>.Value(this, () => false);
    public IState<bool> ShowDebug => State<bool>.Value(this, () => this.settingsService.IsDebugMode()); // TODO: make this a user setting that can be toggled on the UI, and persisted like the custom key settings.
    private List<string> _localUserMessages;
    private ECUActionArguments _actionArguments = new();

    public ControllerActionSetupModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter loggerAdapter,
        IPlatformService platformService,
        IDispatcher dispatcher)
    {
        this.navigator = navigator;
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        // Fire-and-forget initialization
        _ = this.Path.SetAsync(this.settingsService.GetLastWrittenFile());
        _ = this.UseCustomKey.SetAsync(this.settingsService.GetUseCustomKey());
        _ = this.CustomKey.SetAsync(this.settingsService.GetCustomKey());
        _ = this.EnableControls(false);
    }

    public bool IsWriteMode => SelectedAction == ControllerActions.Write;

    private string GetStartButtonText()
    {
        switch (SelectedAction)
        {
            case ControllerActions.Undefined:
                throw new InvalidOperationException("SelectedAction was not defined!");
            case ControllerActions.Read:
                return "Start reading";
            case ControllerActions.Write:
                switch (_actionArguments.WriteType)
                {
                    case WriteType.Test:
                        return "Start Test";
                    case WriteType.Compare:
                        return "Start Comparison";
                    default:
                        return "Start Writing";
                }
                default:
                throw new InvalidOperationException("Invalid index of ControllerActions!");
        }
    }

    private string GetCalibrationOnlyCheckboxText()
    {
        switch (_actionArguments.WriteType)
        {
            case WriteType.Test:
                return "Test Calibration Only (if possible)";
            case WriteType.Compare:
                return "Compare Calibration Only (if possible)";
            default:
                return "Write Calibration Only (if possible)";
        }
    }

    private string GetActivityText()
    {
        switch (SelectedAction)
        {
            case ControllerActions.Undefined:
                throw new InvalidOperationException("SelectedAction was not defined!");
            case ControllerActions.Read:
                return "Reading";
            case ControllerActions.Write:
                switch (_actionArguments.WriteType)
                {
                    case WriteType.Test:
                        return "Testing";
                    case WriteType.Compare:
                        return "Verifying";
                    default:
                        return "Writing";
                }
            default:
                throw new InvalidOperationException("Invalid index of ControllerActions!");
        }
    }

    private async Task<string> GetCurrentHardwareType()
    {
        ECUBase ecu = this.connectionService.GetConnectedECU();
        if(ecu != null)
        {
            if (ecu.HardwareType != PcmType.Undefined)
            {
                await IsHardwareSelectable.SetAsync(false);
                return Enum.GetName(ecu.HardwareType) ?? string.Empty;
            }
        }
        await IsHardwareSelectable.SetAsync(true);
        return Enum.GetName(PcmType.Undefined) ?? string.Empty;
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

    private ValueTask<IImmutableList<string>> GetHardwareTypes(CancellationToken ct)
    {
        List<string> hardwareTypes = [.. Enum.GetNames<PcmType>()];
        IImmutableList<string> res = ImmutableList.CreateRange(hardwareTypes);
        return ValueTask.FromResult(res);

    }

    private ValueTask<IImmutableList<string>> GetWriteTypes(CancellationToken ct)
    {
        List<string> writeTypes = [.. Enum.GetNames<WriteType>()];
        IImmutableList<string> res = ImmutableList.CreateRange(writeTypes);
        return ValueTask.FromResult(res);

    }

    private async Task EnableControls(bool busy)
    {
        await this.StartEnabled.SetAsync(!busy);
        await this.UseCustomKeyEnabled.SetAsync(!busy);
        await this.CustomKeyEnabled.SetAsync(!busy);

        if ((_actionArguments.WriteType == WriteType.Test) || (_actionArguments.WriteType == WriteType.Compare))
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
#if ANDROID
        await Platforms.Android.PermissionMethods.ExtractKernelsToFileAndroid();
#endif
        await this.EnableControls(true);
        string? path = string.Empty;

        if (_actionArguments.StorageFileObject == null)
        {
            await this.ChooseFile();
            if (_actionArguments.StorageFileObject == null)
            {
                await this.AddUserMessage("No file selected.");
                await this.StartEnabled.SetAsync(true);
                return;
            }
            await this.Path.SetAsync(path);
        }

        string customKeyString = await this.CustomKey.Value() ?? String.Empty;
        uint customKey = 0;
            if (UInt32.TryParse(customKeyString,
                System.Globalization.NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out customKey))
            {}
        if(!await this.UseCustomKey.Value())
        {
            customKey = 0;
        }
        ControllerActionModel.ECUActionArguments = new ECUActionArguments()
        {
            SelectedAction = ControllerActionSetupModel.SelectedAction,
            HardwareType = Enum.Parse<PcmType>(await SelectedHardwareType.Value() ?? "Undefined"),
            WriteType = (WriteType)Enum.Parse(typeof(WriteType), await SelectedWriteType.Value()),
            UseHighSpeed = await this.UseHighSpeed.Value(), // We can safely use this like an override, since it was set to device prefrences on page load. User selection beyond that will reflect here.
            ShowDebug = await this.ShowDebug.Value(),
            CustomKey = customKey,
            ContentStream = _actionArguments.ContentStream,
            StorageFileObject = _actionArguments.StorageFileObject
        };

        this.tokenSource = new CancellationTokenSource();
        CancellationToken writeCancellationToken = this.tokenSource.Token;
        try
        {
            await this.navigator.NavigateViewModelAsync<ControllerActionModel>(this, cancellation: writeCancellationToken);
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
            this.tokenSource = null;
            await this.EnableControls(false);
        }
    }

    private async Task PerformWrite(WriteManager writeManager)
    {
        await writeManager.Begin(_actionArguments.ContentStream);
#if !ANDROID
        await this.AddUserMessage("Write succeeded!");
        this.tokenSource = null;
        await this.EnableControls(false);
#endif
    }

    [Command]
    public async Task ChooseFile()
    {
        try
        {
            await this.StartEnabled.SetAsync(false);
            switch (SelectedAction)
            {
                case ControllerActions.Read:
                    _actionArguments.StorageFileObject = await this.platformService.PromptForFileSavePath();
                    break;
                case ControllerActions.Write:
                    _actionArguments.StorageFileObject = await this.platformService.PromptForFileOpenPath();
                    break;
            }
            if (_actionArguments.StorageFileObject != null)
            {
                await this.Path.SetAsync(((StorageFile)_actionArguments.StorageFileObject).Name);
            }
        }
        finally
        { 
            await this.StartEnabled.SetAsync(true);
        }
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
        _localUserMessages.Add(message);
        await this.UserLog.SetAsync(_localUserMessages.ToArray().JoinBy("\r\n"));
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
