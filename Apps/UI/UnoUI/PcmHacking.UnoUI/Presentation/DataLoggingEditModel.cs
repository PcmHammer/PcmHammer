using Microsoft.UI.Dispatching;
using Uno.Extensions.Navigation;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingEditModel()
{
    private DataLoggingEditContext editContext;

    public IListState<Parameter> ParameterList => ListState<Parameter>.Empty(this);
    public IListState<Conversion> ConversionList => ListState<Conversion>.Empty(this);
    public IState<bool> Zoom => State<bool>.Value(this, () => false);

    public DataLoggingEditModel(
        DataLoggingEditContext editContext) : this()
    {
        this.editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
    }

    public async Task Initialize()
    {
        await this.ParameterList.Update(updater: existing => editContext.Database.ListParametersBySupportedOs(editContext.Osid).ToImmutableList(), ct: CancellationToken.None);
        await this.ConversionList.Update(updater: existing => (this.editContext.LogColumn.Parameter?.Conversions ?? new Conversion[0]).ToImmutableList(), ct: CancellationToken.None);
        await this.Zoom.SetAsync(this.editContext.LogColumn.Zoom);
        await this.ParameterList.TrySelectAsync(this.editContext.LogColumn.Parameter);
        await this.ConversionList.TrySelectAsync(this.editContext.LogColumn.Conversion);
    }
}
