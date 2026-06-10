// SPDX-License-Identifier: GPL-3.0-only
using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record MenuModel
{
    private readonly INavigator navigator;
    private readonly INoticeService noticeService;
    private readonly IConnectionService connectionService;
    private readonly IDispatcher _dispatcher;

    public IState<string> HelpButtonText => State<string>.Value(this, () => String.Empty);

    public IState<bool> ActionButtonsEnabled => State<bool>.Value(this, () => false);

    public IState<bool> AllButtonsEnabled => State<bool>.Value(this, () => true)
        .ForEach(async (state, ct) => await UpdateActionButtonStates(ct));

    public MenuModel(
        IStringLocalizer localizer,
        INavigator navigator,
        INoticeService noticeService,
        IConnectionService connectionService,
        IDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.noticeService = noticeService ?? throw new ArgumentNullException(nameof(noticeService));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.noticeService.NoticeData.ForEach(async (notice, ct) => await this.HelpButtonText.SetAsync(notice.HelpButtonText ?? String.Empty));
        var _ = this.noticeService.GetNotice();
        connectionService.ConnectionState.ForEach(async (state, ct) => await this.UpdateActionButtonStates(ct));
    }

    private async Task UpdateActionButtonStates(CancellationToken ct)
    {
        ConnectionStates currentState = await this.connectionService.ConnectionState.Value(ct);
        bool resetting = connectionService.ResetTimeRemaining > 0;
        await ActionButtonsEnabled.SetAsync(!resetting && currentState >= ConnectionStates.Connected);
    }

    public async Task GoToDataLogging()
    {
        await this.navigator.NavigateViewModelAsync<DataLoggingModel>(this);
    }

    public async Task GoToSettings()
    {
        await this.navigator.NavigateViewModelAsync<SettingsModel>(this);
    }


    public void ButtonStateControl(bool buttonsEnabled)
    {
        _ = AllButtonsEnabled.SetAsync(buttonsEnabled);
    }

    public async Task GoToExit()
    {
        App.ApplicationShutdownSource.Cancel();
        await _dispatcher.ExecuteAsync(async () =>
        {
            await App.GetService<IConnectionService>().AwaitConnectionShutdown();
            Application.Current?.Exit();
        });
    }

    public async Task GoToControllerFunctions()
    {
        await this.navigator.NavigateViewModelAsync<ControllerFunctionsModel>(this);
    }

    public async Task GoToHelp()
    {
        await this.navigator.NavigateViewModelAsync<HelpModel>(this);
    }
}
