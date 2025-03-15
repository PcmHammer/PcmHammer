using System.Drawing.Text;

namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingParametersPage : Page
{
    private DataLoggingParametersModel? model;

    public DataLoggingParametersPage()
    {
        this.InitializeComponent();

        this.DataContextChanged += (sender, e) =>
        {
            var newModel = (this.DataContext as DataLoggingParametersViewModel)?.Model as DataLoggingParametersModel;
            if (newModel == null)
            {
                return;
            }

            if (newModel == this.model)
            {
                return;
            }

            this.model = newModel;

            this.InitializeParameters();
        };
    }

    private void InitializeParameters()
    {
        if (this.model == null)
        {
            return;
        }

        
    }
}

