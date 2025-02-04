using CommunityToolkit.Mvvm.Messaging;
using PcmHacking.UnoUI.Services;
using Uno.Extensions.Navigation;

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
        WriteTypeEntity writeTypeEntity = new(WriteType.Full);
        await this.navigator.NavigateDataAsync(this, data: writeTypeEntity);
    }

    public async Task GoToTestWrite()
    {
        WriteTypeEntity writeTypeEntity = new(WriteType.TestWrite);
        await this.navigator.NavigateDataAsync(this, data: writeTypeEntity);
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
