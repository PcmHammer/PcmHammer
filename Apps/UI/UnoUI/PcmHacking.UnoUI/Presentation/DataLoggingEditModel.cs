using Microsoft.UI.Dispatching;
using Uno.Extensions.Navigation;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingEditModel()
{
    private readonly INavigator navigator;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly LogColumn logColumn;

    public IListState<Parameter> ParameterList => ListState<Parameter>.Empty(this);
    public IListState<Conversion> ConversionList => ListState<Conversion>.Empty(this);
    public IState<bool> Zoom => State<bool>.Value(this, () => false);

    public DataLoggingEditModel(
        INavigator navigator,
        DispatcherQueue dispatcherQueue,
        DataLoggingEditContext editContext) : this()
    {
        this.navigator = navigator;
        this.dispatcherQueue = dispatcherQueue;
        this.logColumn = editContext.LogColumn;
        this.ParameterList.Update(updater: existing => editContext.Database.ListParametersBySupportedOs(editContext.Osid).ToImmutableList(), ct: CancellationToken.None);
        this.ConversionList.Update(updater: existing => (this.logColumn.Parameter?.Conversions ?? new Conversion[0]).ToImmutableList(), ct: CancellationToken.None);
        this.Zoom.SetAsync(this.logColumn.Zoom);
    }

    public async Task SetSelection()
    {
        await this.ParameterList.TrySelectAsync(this.logColumn.Parameter);
        await this.ConversionList.TrySelectAsync(this.logColumn.Conversion);
    }

    [Command]
    public async Task OkClicked()
    {
        await this.navigator.GoBack(this);
    }

    [Command]
    public async Task CancelClicked()
    {
        await this.navigator.GoBack(this);
    }
}
