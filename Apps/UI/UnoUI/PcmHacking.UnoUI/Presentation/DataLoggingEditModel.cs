using Microsoft.UI.Dispatching;
using Uno.Extensions.Navigation;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingEditModel()
{
    private ParameterEditContext editContext;

    public IListState<Parameter> ParameterList => ListState<Parameter>.Empty(this);
    public IListState<Conversion> ConversionList => ListState<Conversion>.Empty(this);
    public IState<bool> Zoom => State<bool>.Value(this, () => false);
    public IState<Parameter> SelectedParameter => State<Parameter>.Value(this, () => null);
    public IState<Visibility> DeleteButtonVisibility => State<Visibility>.Value(this, ()=> Visibility.Visible);

    public DataLoggingEditModel(
        ParameterEditContext editContext) : this()
    {
        this.editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
    }

    public async Task Initialize()
    {
        await this.ParameterList.Update(updater: existing => editContext.Database.ListParametersBySupportedOs(editContext.Osid).ToImmutableList(), ct: CancellationToken.None);
        await this.SelectedParameter.ForEach(SelectedParameterChanged);
        await this.ParameterList.Selection(SelectedParameter);        

        if (this.editContext.Input != null)
        {
            await this.Zoom.SetAsync(this.editContext.Input.Zoom);
            await this.ConversionList.Update(updater: existing => (this.editContext.Input.Parameter?.Conversions ?? new Conversion[0]).ToImmutableList(), ct: CancellationToken.None);
            await this.ParameterList.TrySelectAsync(this.editContext.Input.Parameter);
            await this.ConversionList.TrySelectAsync(this.editContext.Input.Conversion);
            await this.DeleteButtonVisibility.SetAsync(Visibility.Visible);
        }
        else
        {
            // TODO: select the first parameter - await this.ParameterList.TrySelectAsync(how?);
            await this.DeleteButtonVisibility.SetAsync(Visibility.Collapsed);
        }        
    }

    private async ValueTask SelectedParameterChanged(Parameter? newValue, CancellationToken ct)
    {
        if (newValue == null)
        {
            await this.ConversionList.Update(updater: existing => ImmutableList<Conversion>.Empty, ct);
            return;
        }

        await this.ConversionList.Update(updater: existing => newValue.Conversions.ToImmutableList(), ct);
        
        // TODO: wait for a signal from the update callback
        await Task.Delay(100);
        
        var firstConversion = newValue.Conversions.FirstOrDefault();
        if (firstConversion != null)
        {
            bool result = await this.ConversionList.TrySelectAsync(firstConversion);
            result.ToString();
        }
    }

    public async Task CreateOutput()
    {
        var selectedParameter = await this.ParameterList.GetSelectedItem();
        if (selectedParameter == null)
        {
            return;
        }

        var selectedConversion = await this.ConversionList.GetSelectedItem();
        if (selectedConversion == null)
        {
            return;
        }

        this.editContext.Output = new LogColumn(selectedParameter, selectedConversion, await this.Zoom.Value());
    }
}
