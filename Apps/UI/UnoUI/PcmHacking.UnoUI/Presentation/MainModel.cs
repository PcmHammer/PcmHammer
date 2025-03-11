using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record MainModel
{
    private INavigator navigator;
    private ISettingsService settingsService;
    private IConnectionService vehicleService;

    public MainModel(
        IStringLocalizer localizer,
        INavigator navigator,
        ISettingsService settingsService,
        IConnectionService vehicleService)
    {
        this.navigator = navigator;
        this.settingsService = settingsService;
        this.vehicleService = vehicleService;
        this.Title = localizer["ApplicationName"];

        vehicleService.ConnectionState.ForEach((state, ct) => this.ConnectionStateChanged(ct));

        // This is a bit of a hack. We need to update the displayed settings when the page is loaded.
        // Connection is an async operation, but constructors can't be async, so we fire and forget.
        this.vehicleService.TryConnect(this.settingsService.LoadConnectionSettings());
    }

    public string? Title { get; }
    
    [Command]
    public async ValueTask GoBack()
    {
        await this.navigator.GoBack(this);
        await this.UpdateBackButtonState(CancellationToken.None);
    }

    /// <summary>
    /// This doesn't work, not sure if Uno Platform bug or if I'm doing something wrong.
    /// </summary>
    /// <remarks>
    /// This requires the Frame element to have Navigated="{Binding FrameNavigated}" in XAML but that creates a build error.
    /// https://github.com/unoplatform/uno/issues/19343
    /// Adding a "NavigationEventArgs args" parameter doesn't fix the build error.
    /// 
    /// Also change the button's IsEnabled property to:
    ///     IsEnabled="{Binding BackButtonEnabled}" 
    /// The workaround for now is:
    ///     IsEnabled="{Binding CanGoBack, ElementName=ContentFrame, Mode=OneWay}"
    ///     
    /// Also uncomment the call to UpdateBackButtonState in ConnectionStateChanged.
    /// 
    /// Also set BackButtonEnabled to be false by default.
    /// 
    /// Unfortunately without the FrameNavigated handler, the back button 
    /// stays disabled when you navigate to other pages.
    /// </remarks>
    [Command]
    public async ValueTask FrameNavigated()
    {
        await this.UpdateBackButtonState(CancellationToken.None);
    }

    public IState<string> SerialPortName => State<string>.Value(this, () => string.Empty);

    public IState<string> DeviceName => State<string>.Value(this, () => string.Empty);

    public IState<string> ConnectionState => State<string>.Value(this, () => string.Empty);

    public IState<string> ConnectionError => vehicleService.ConnectionError;

    public IState<string> OperatingSystemId => vehicleService.OperatingSystemId;

    public IState<string> Voltage => vehicleService.Voltage;

    /// <summary>
    /// See comments on FrameNavigated above.
    /// </summary>
    public IState<bool> BackButtonEnabled => State<bool>.Value(this, () => false);

    private async ValueTask UpdateDisplayedSettings(CancellationToken ct)
    {
        if (await this.settingsService.GetObd2DeviceCategory(ct) == "J2534")
        {
            await this.SerialPortName.SetAsync("Not Used");
            await this.DeviceName.SetAsync(await this.settingsService.GetJ2534DeviceName(ct));
        }
        else
        {
            await this.SerialPortName.SetAsync(await this.settingsService.GetObd2SerialPortName(ct));
            await this.DeviceName.SetAsync(await this.settingsService.GetObd2SerialDeviceName(ct));
        }
    }

    private async Task UpdateBackButtonState(CancellationToken ct)
    {
        ConnectionStates currentState = await this.vehicleService.ConnectionState.Value(ct);
        string currentActivity = await this.vehicleService.Activity.Value(ct) ?? String.Empty;
        bool connectionNotActive = currentState != ConnectionStates.Active;
        bool justPolling = currentActivity == ConnectionService.PollingActivity;
        bool canGoBack = await this.navigator.CanGoBack();
        bool backButtonEnabled = canGoBack && (connectionNotActive || justPolling);

        // Enable/disable the back button depending on whether the connection state is Active.
        if (await this.BackButtonEnabled.Value() != backButtonEnabled)
        {
            await this.BackButtonEnabled.SetAsync(backButtonEnabled);
        }
    }

    private async ValueTask ConnectionStateChanged(CancellationToken ct)
    {
        // See comments on FrameNavigated above.
        await UpdateBackButtonState(ct);
        ConnectionStates currentState = await this.vehicleService.ConnectionState.Value(ct);
        switch (currentState)
        {
            case ConnectionStates.NotConfigured:
                await this.ConnectionState.SetAsync("Not Configured");
                break;
            case ConnectionStates.NotConnected:
                await this.ConnectionState.SetAsync("Not Connected");
                break;
            case ConnectionStates.Connecting:
                await this.ConnectionState.SetAsync("Connecting");
                break;
            case ConnectionStates.Connected:
                await this.ConnectionState.SetAsync("Connected");
                await this.UpdateDisplayedSettings(ct);
                break;
            case ConnectionStates.Active:
                await this.ConnectionState.SetAsync(await this.vehicleService.Activity.Value());
                break;
        }
    }
}
