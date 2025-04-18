namespace PcmHacking.UnoUI.Presentation;

public sealed partial class OtherFunctionsPage : Page
{
    private OtherFunctionsModel? model;

    public OtherFunctionsPage()
    {
        this.InitializeComponent();

        this.DataContextChanged += (sender, e) =>
        {
            this.model = (this.DataContext as OtherFunctionsViewModel)?.Model as OtherFunctionsModel ?? this.model;
        };
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        this.model?.NavigatedAway();
    }
}

