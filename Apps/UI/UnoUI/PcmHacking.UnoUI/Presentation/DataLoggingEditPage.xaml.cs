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

    private async void ContentDialog_Loaded(object sender, RoutedEventArgs e)
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
    
    private async void Apply_Clicked(object sender, RoutedEventArgs e)
    {
        var vm = this.DataContext as DataLoggingEditViewModel;
        var model = vm?.Model as DataLoggingEditModel;
        if (model == null)
        {
            return;
        }

        if (this.OnApply != null)
        {
            await model.CreateOutput();
            this.OnApply(sender, new System.EventArgs());
        }
        this.Hide();
    }

    private void Delete_Clicked(object sender, RoutedEventArgs e)
    {
        if (this.OnDelete != null)
        {
            this.OnDelete(sender, new System.EventArgs());
        }
        this.Hide();
    }
    private void Cancel_Clicked(object sender, RoutedEventArgs e)
    {
        if (this.OnCancel != null)
        {
            this.OnCancel(sender, new System.EventArgs());
        }
        this.Hide();
    }
}

