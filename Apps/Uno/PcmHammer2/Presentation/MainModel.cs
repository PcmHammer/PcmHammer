namespace UnoExperiment1.Presentation;

public partial record MainModel
{
    private INavigator _navigator;

    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        INavigator navigator)
    {
        _navigator = navigator;
        Title = localizer["ApplicationName"] + " - Release 25";
    }

    public string? Title { get; }

    public IState<string> Name => State<string>.Value(this, () => string.Empty);

    public async Task GoToSecond()
    {
        var name = await Name;
        await _navigator.NavigateViewModelAsync<SecondModel>(this, data: new Entity(name!));
    }

    public async Task GoToSettings()
    {
        var name = await Name;
        await _navigator.NavigateViewModelAsync<SettingsModel>(this);
    }

    public async Task GoToWrite()
    {
        var name = await Name;
        await _navigator.NavigateViewModelAsync<WriteModel>(this);
    }

    public async Task GoToRead()
    {
        var name = await Name;
        await _navigator.NavigateViewModelAsync<ReadModel>(this);
    }
}
