using System;
using Windows.System;

namespace PcmHacking.UnoUI.Presentation;

public partial record HelpModel
{
    private readonly INavigator navigator;

    IState<string> Message => State<string>.Value(this, () => "TODO: Add help text here.");

    public HelpModel(INavigator navigator)
    {
        this.navigator = navigator;
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
