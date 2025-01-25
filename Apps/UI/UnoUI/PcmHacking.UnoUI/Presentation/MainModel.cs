using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record MainModel
{
    private INavigator navigator;

    public MainModel(
        IStringLocalizer localizer,
        INavigator navigator)
    {
        this.navigator = navigator;
        this.Title = localizer["ApplicationName"];
    }

    public string? Title { get; }
    
    [Command]
    public async ValueTask GoBack()
    {
        await this.navigator.GoBack(this);
    }
}
