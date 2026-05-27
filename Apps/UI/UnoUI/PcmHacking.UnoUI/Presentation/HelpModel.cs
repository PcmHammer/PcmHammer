// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using Windows.System;

namespace PcmHacking.UnoUI.Presentation;

public partial record HelpModel
{
    private readonly INavigator navigator;
    public IState<string> Message => State<string>.Value(this, () => String.Empty);
    public IState<string> Link => State<string>.Value(this, () => "http://pcmhammer.org");
    public IState<string> LinkText => State<string>.Value(this, () => "Visit the wiki.");
    public IState<Visibility> LinkVisibility => State<Visibility>.Value(this, () => Visibility.Collapsed);

    public HelpModel(INavigator navigator, INoticeService noticeService, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        this.navigator = navigator;
        dispatcherQueue.TryEnqueue(async () => await UpdateUI(noticeService));
    }

    private async Task UpdateUI(INoticeService noticeService)
    {
        NoticeData noticeData = await noticeService.NoticeData.Value();
        await Message.SetAsync(noticeData.NoticeText);
        await Link.SetAsync(noticeData.LinkUrl);
        await LinkText.SetAsync(noticeData.LinkText);
        if (!string.IsNullOrEmpty(noticeData.LinkUrl))
        {
            await LinkVisibility.SetAsync(Visibility.Visible);
        }
    }

    public async Task OpenForum()
    {
        await Launcher.LaunchUriAsync(new Uri("https://pcmhacking.net/forums/"));
    }

    public async Task OpenGitHub()
    {
        await Launcher.LaunchUriAsync(new Uri("https://github.com/PcmHammer/PcmHammer"));
    }

    public async Task GoToLog()
    {
        await this.navigator.NavigateViewModelAsync<LogModel>(this);
    }
}
