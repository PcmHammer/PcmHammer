namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingEditPage : ContentDialog
{
    public event EventHandler OnApply;
    public event EventHandler OnDelete;
    public event EventHandler OnCancel;

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

        await model.Initialize();
        this.Parameters.ScrollIntoView(this.Parameters.SelectedItem);
        this.Conversions.ScrollIntoView(this.Conversions.SelectedItem);
    }
    
    private void Apply_Clicked(object sender, RoutedEventArgs e)
    {
        if (this.OnApply != null)
        {
            this.OnApply(sender, e);
        }
        this.Hide();
    }

    private void Delete_Clicked(object sender, RoutedEventArgs e)
    {
        if (this.OnDelete != null)
        {
            this.OnDelete(sender, e);
        }
        this.Hide();
    }
    private void Cancel_Clicked(object sender, RoutedEventArgs e)
    {
        if (this.OnCancel != null)
        {
            this.OnCancel(sender, e);
        }
        this.Hide();
    }
}

