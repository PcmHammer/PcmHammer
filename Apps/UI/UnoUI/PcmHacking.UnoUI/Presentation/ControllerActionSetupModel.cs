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

public record ActionResult(bool Proceed = false, ECUActionArguments? Arguments = null);
public partial record ControllerActionSetupModel
{
    public static ControllerActions SelectedAction = ControllerActions.Write;

    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly ISettingsService settingsService;
    private readonly LoggerAdapter loggerAdapter;
    private readonly IPlatformService platformService;

    private CancellationTokenSource? tokenSource;
    const string defaultPath = "No file selected.";

    public string Title { get { return "Write PCM"; } }

    public IListFeed<string> HardwareTypes => ListFeed<string>.Async(ct => this.GetHardwareTypes(ct)).Selection(SelectedHardwareType);
    public IState<string> SelectedHardwareType => State<string>.Value(this, () => this.GetCurrentHardwareType().Result);

    public IListFeed<string> WriteTypes => ListFeed<string>.Async(ct => this.GetWriteTypes(ct)).Selection(SelectedWriteType);
    public IState<string> SelectedWriteType => State<string>.Value(this, () => { return Enum.GetName(WriteType.Test) ?? "Test"; })
        .ForEach(WriteTypeChanged);

    public IState<bool> StartEnabled => State<bool>.Value(this, () => true);
    public IState<bool> CancelEnabled => State<bool>.Value(this, () => false);

    public IState<string> Path => State<string>.Value(this, () => defaultPath);
    public IState<string> StartButtonText => State<string>.Value(this, () => this.GetStartButtonText());

    public IState<bool> UseCustomKey => State<bool>.Value(this, () => false).ForEach((value, ct) => UseCustomKeyChanged(value, ct));
    public IState<bool> UseCustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<string> CustomKey => State<string>.Value(this, () => "");
    public IState<bool> CustomKeyEnabled => State<bool>.Value(this, () => true);
    public IState<bool> UseHighSpeed => State<bool>.Value(this, () => this.settingsService.Is4xReadWriteEnabled());
    public IState<bool> IsHardwareSelectable => State<bool>.Value(this, () => false);
    public IState<bool> ShowDebug => State<bool>.Value(this, () => this.settingsService.IsDebugMode()); // TODO: make this a user setting that can be toggled on the UI, and persisted like the custom key settings.
    private ECUActionArguments _actionArguments = new();

    public ControllerActionSetupModel(
        INavigator navigator,
        IConnectionService connectionService,
        ISettingsService settingsService,
        LoggerAdapter loggerAdapter,
        IPlatformService platformService)
    {
        this.navigator = navigator;
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.loggerAdapter = loggerAdapter ?? throw new ArgumentNullException(nameof(loggerAdapter));
        this.platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));

        // Fire-and-forget initialization
        _ = this.Path.SetAsync(this.settingsService.GetLastWrittenFile());
        _ = this.UseCustomKey.SetAsync(this.settingsService.GetUseCustomKey());
        _ = this.CustomKey.SetAsync(this.settingsService.GetCustomKey());
        _ = this.EnableControls(false);
    }

    public bool IsWriteMode => SelectedAction == ControllerActions.Write;

    private async ValueTask WriteTypeChanged(string? newValue, CancellationToken ct) 
    {
        _actionArguments.WriteType = Enum.Parse<WriteType>(newValue);
    }

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
                        return "Start Test write";
                    case WriteType.Compare:
                        return "Start Comparison";
                    default:
                        return "Start Writing";
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
        hardwareTypes.Remove(Enum.GetName<PcmType>(PcmType.Unsupported) ?? ""); // Leave `Undefined` as a placeholder, remove this as it should never be a selection.
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
    }

    [Command]
    public async ValueTask Cancel(CancellationToken ct)
    {
        await this.navigator.NavigateBackWithResultAsync<ActionResult>(this, data: new ActionResult(false, null));
    }

    [Command]
    public async ValueTask Start(CancellationToken cancellationToken)
    {
        await this.EnableControls(true);
        string? path = string.Empty;

        if (_actionArguments.StorageFileObject == null)
        {
            await this.ChooseFile();
            if (_actionArguments.StorageFileObject == null)
            {
                this.loggerAdapter.AddUserMessage("No file selected.");
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

        // Set the static object to be passed to the action model.
        ECUActionArguments args = new()
        {
            SelectedAction = ControllerActionSetupModel.SelectedAction,
            HardwareType = Enum.Parse<PcmType>(await SelectedHardwareType.Value() ?? "Undefined"),
            WriteType = Enum.Parse<WriteType>(await SelectedWriteType.Value()),
            UseHighSpeed = await this.UseHighSpeed.Value(), // We can safely use this like an override, since it was set to device preferences on page load. User selection beyond that will reflect here.
            ShowDebug = await this.ShowDebug.Value(),
            CustomKey = customKey,
            ContentStream = _actionArguments.ContentStream,
            StorageFileObject = _actionArguments.StorageFileObject
        };
        await this.navigator.NavigateBackWithResultAsync<ActionResult>(this, data: new ActionResult(true, args));
    }

    // This variant of ChooseFile selects the proper picker strategy using the set SelectedAction.
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
}
