using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Presentation;

public partial record MenuModel
{
    private INavigator navigator;

    public MenuModel(
        IStringLocalizer localizer,
        INavigator navigator)
    {
        this.Title = localizer["ApplicationName"];
        this.navigator = navigator;
    }

    public string? Title { get; }

    public IState<string> Name => State<string>.Value(this, () => string.Empty);

    public async Task GoToSecond()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<SecondModel>(this, data: new Entity(name!));
    }

    public async Task GoToSettings()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<SettingsModel>(this);
    }

    public async Task GoToWrite()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<WriteModel>(this);
    }

    public async Task GoToRead()
    {
        var name = await Name;
        await this.navigator.NavigateViewModelAsync<ReadModel>(this);
    }
}
