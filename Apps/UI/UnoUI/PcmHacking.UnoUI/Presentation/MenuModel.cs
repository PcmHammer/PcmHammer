using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Navigation;

namespace PcmHacking.UnoUI.Presentation;

public partial record MenuModel
{
    private readonly INavigator navigator;
    private readonly INoticeService noticeService;

    public IState<string> HelpButtonText => State<string>.Value(this, () => String.Empty);

    public MenuModel(
        IStringLocalizer localizer,
        INavigator navigator,
        INoticeService noticeService)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.noticeService = noticeService ?? throw new ArgumentNullException(nameof(noticeService));
        this.noticeService.NoticeData.ForEach(async (notice, ct) => await this.HelpButtonText.SetAsync(notice.HelpButtonText ?? String.Empty));
        var _ = this.noticeService.GetNotice();
    }

    public async Task GoToDataLogging()
    {
        await this.navigator.NavigateViewModelAsync<DataLoggingModel>(this);
    }

    public async Task GoToSettings()
    {
        await this.navigator.NavigateViewModelAsync<SettingsModel>(this);
    }

    public async Task GoToExit()
    {
        App.ApplicationShutdownSource.Cancel();
        await Task.Delay(1000);
        Environment.Exit(0);
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
