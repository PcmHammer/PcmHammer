// SPDX-License-Identifier: GPL-3.0-only
using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record MainModel
{
    public static bool CanGoBack { get; private set; }
    public bool IsBackButtonVisible = true;
    private INavigator navigator;
    private ISettingsService settingsService;
    private IConnectionService connectionService;

    public MainModel(
        IStringLocalizer localizer,
        INavigator navigator,
        ISettingsService settingsService,
        IConnectionService vehicleService)
    {
        this.navigator = navigator;
        this.settingsService = settingsService;
        this.connectionService = vehicleService;
        this.Title = localizer["ApplicationName"];

        vehicleService.ConnectionState.ForEach((state, ct) => this.ConnectionStateChanged(ct));

        // This updates the UI with the latest configuration when the connection service attempts to connect.
        this.connectionService.DeviceName.ForEach((port, ct) => this.DeviceName.SetAsync(port, ct));
        this.connectionService.DeviceState.ForEach((device, ct) => this.DeviceStatus.SetAsync(device, ct));

        // Connection is an async operation, but constructors can't be async, so we fire and forget.
        // This is a bit of a hack, but I don't see any real issues from it, and we want to update
        // the displayed settings and test the connection as soon as the page is loaded.
        this.connectionService.TryConnect(this.settingsService.LoadConnectionSettings());
#if ANDROID
        IsBackButtonVisible = false;
#endif
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

    public IState<string> DeviceName => State<string>.Value(this, () => string.Empty);

    public IState<string> DeviceStatus => State<string>.Value(this, () => string.Empty);

    public IState<string> ConnectionState => State<string>.Value(this, () => string.Empty);

    public IState<string> ConnectionError => connectionService.ConnectionError;

    public IState<string> OperatingSystemId => connectionService.OperatingSystemId;

    public IState<string> Voltage => connectionService.Voltage;

    public IState<string> Bus => connectionService.Bus;

    /// <summary>
    /// See comments on FrameNavigated above.
    /// </summary>
    public IState<bool> BackButtonEnabled => State<bool>.Value(this, () => false);

    private async Task UpdateBackButtonState(CancellationToken ct)
    {
        ConnectionStates currentState = await this.connectionService.ConnectionState.Value(ct);
        string currentActivity = await this.connectionService.Activity.Value(ct) ?? String.Empty;
        bool connectionNotActive = currentState != ConnectionStates.Active;
        bool justPolling = currentActivity == ConnectionService.PollingActivity;
        bool canGoBack = await this.navigator.CanGoBack();
        CanGoBack = canGoBack && (connectionNotActive || justPolling);

        // Enable/disable the back button depending on whether the connection state is Active.
        if (await this.BackButtonEnabled.Value() != CanGoBack)
        {
            await this.BackButtonEnabled.SetAsync(CanGoBack);
        }
    }

    private async ValueTask ConnectionStateChanged(CancellationToken ct)
    {
        // See comments on FrameNavigated above.
        await UpdateBackButtonState(ct);
        ConnectionStates currentState = await this.connectionService.ConnectionState.Value(ct);
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
                if(this.connectionService.ResetTimeRemaining != -1)
                {
                    await this.ConnectionState.SetAsync($"Resetting {this.connectionService.ResetTimeRemaining}...");
                    break;
                }
                await this.ConnectionState.SetAsync("Connected");
                break;
            case ConnectionStates.Active:
            case ConnectionStates.Logging:
                string state = await this.connectionService.Activity.Value(ct) ?? String.Empty;
                await this.ConnectionState.SetAsync(state);
                break;
        }
    }
}
