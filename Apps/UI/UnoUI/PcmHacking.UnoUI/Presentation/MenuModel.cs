// SPDX-License-Identifier: GPL-3.0-only
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

    public async Task GoToWrite()
    {
        // Can't get this to work the right way.
        // Not sure if I'm doing it wrong or if this is another bug in Uno.
        //WriteTypeEntity writeTypeEntity = new(WriteType.Full);
        //await this.navigator.NavigateDataAsync(this, data: writeTypeEntity);
        WriteModel.WriteType = WriteType.Full;
        await this.navigator.NavigateViewModelAsync<WriteModel>(this);
    }

    public async Task GoToTestWrite()
    {
        // Can't get this to work the right way.
        // Not sure if I'm doing it wrong or if this is a bug in Uno.
        // 
        // Note that this requires a corresponding change in RegisterRoutes in App.xaml.cs.
        //
        // WriteTypeEntity writeTypeEntity = new(WriteType.TestWrite);
        // await this.navigator.NavigateDataAsync(this, data: writeTypeEntity);
        //
        // Until I can figure out why the above isn't working, here's a hacky workaround:
        WriteModel.WriteType = WriteType.TestWrite;
        await this.navigator.NavigateViewModelAsync<WriteModel>(this);
    }

    public async Task GoToOtherFunctions()
    {
        await this.navigator.NavigateViewModelAsync<OtherFunctionsModel>(this);
    }

    public async Task GoToHelp()
    {
        await this.navigator.NavigateViewModelAsync<HelpModel>(this);
    }
}
