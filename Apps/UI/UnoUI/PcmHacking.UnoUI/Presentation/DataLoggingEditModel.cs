using Microsoft.UI.Dispatching;
using Uno.Extensions.Navigation;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingEditModel()
{
    private readonly INavigator navigator;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly DataLoggingEditContext editContext;

    public IListState<Parameter> ParameterList => ListState<Parameter>.Empty(this);
    public IListState<Conversion> ConversionList => ListState<Conversion>.Empty(this);
    public IState<bool> Zoom => State<bool>.Value(this, () => false);

    public DataLoggingEditModel(
        INavigator navigator,
        DispatcherQueue dispatcherQueue,
        DataLoggingEditContext editContext) : this()
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        this.editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
        this.ParameterList.Update(updater: existing => editContext.Database.ListParametersBySupportedOs(editContext.Osid).ToImmutableList(), ct: CancellationToken.None);
        this.ConversionList.Update(updater: existing => (this.editContext.LogColumn.Parameter?.Conversions ?? new Conversion[0]).ToImmutableList(), ct: CancellationToken.None);
        this.Zoom.SetAsync(this.editContext.LogColumn.Zoom);
    }

    public async Task SetSelection()
    {
        await this.ParameterList.TrySelectAsync(this.editContext.LogColumn.Parameter);
        await this.ConversionList.TrySelectAsync(this.editContext.LogColumn.Conversion);
    }

    [Command]
    public async Task ApplyClicked()
    {
        await this.navigator.GoBack(this);
    }

    [Command]
    public async Task DeleteClicked()
    {
        // Can't use this approach because it doesn't include a way to populate the model for the page
        // var confirmationDialog = new DataLoggingDeleteConfirmationDialog(); // ....this.navigator, this.editContext);
        // var result = confirmationDialog.ShowAsync();

        // But this appraoch doesn't provide a built-in way to find out which button the user clicked to dismiss the dialog.
        await this.navigator.NavigateViewModelAsync<DataLoggingDeleteConfirmationModel>(this, data: this.editContext);

    }

    [Command]
    public async Task CancelClicked()
    {
        await this.navigator.GoBack(this);
    }
}
