namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingEditPage : ContentDialog
{
    public DataLoggingEditPage()
    {
        this.InitializeComponent();
    }

    private async Task ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        var vm = this.DataContext as DataLoggingEditViewModel;
        var model = vm?.Model as DataLoggingEditModel;
        if (model == null)
        {
            return;
        }

        await model.SetSelection();
        this.Parameters.ScrollIntoView(this.Parameters.SelectedItem);
        this.Conversions.ScrollIntoView(this.Conversions.SelectedItem);
    }
}

