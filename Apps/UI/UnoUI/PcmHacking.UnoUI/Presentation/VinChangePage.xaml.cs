namespace PcmHacking.UnoUI.Presentation;

public sealed partial class VinChangePage : Page
{
    public VinChangePage()
    {
        this.InitializeComponent();
    }

    void NewVin_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Will need to revise this to support other platforms.
        string newVin = this.NewVin.Text ?? string.Empty;
        var model = (this.DataContext as VinChangeViewModel)?.Model;
        model?.NewVinChanged(newVin, CancellationToken.None);
    }
}

