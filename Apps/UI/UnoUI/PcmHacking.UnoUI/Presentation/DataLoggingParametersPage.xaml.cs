using System.Drawing.Text;

namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingParametersPage : Page
{
    private DataLoggingParametersModel? model;

    public DataLoggingParametersPage()
    {
        this.InitializeComponent();

        this.DataContextChanged += async (sender, e) =>
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

            await this.model.LogProfile.Select((profile) =>
            {
                this.InitializeParameters(profile);
                return true;
            });

            await this.model.Rows.Select((row) =>
            {
                this.UpdateParameterValues(row.Values);
                return true;
            });
        };
    }

    private List<TextBlock> parameterValues;

    private void InitializeParameters(LogProfile profile)
    {
        if (this.model == null)
        {
            return;
        }

        int row = 0;
        foreach(var column in profile.Columns)
        {
            TextBlock name = new TextBlock();
            name.Text = column.Parameter.Name;
            name.Margin = new Thickness(5);
            name.HorizontalAlignment = HorizontalAlignment.Left;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.SetValue(Grid.RowProperty, row);
            name.SetValue(Grid.ColumnProperty, 0);

            TextBlock value = new TextBlock();
            value.Text = column.Conversion.Units;
            value.Margin = new Thickness(5);
            value.HorizontalAlignment = HorizontalAlignment.Left;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.SetValue(Grid.RowProperty, row);
            value.SetValue(Grid.ColumnProperty, 1);
            row++;
            parameterValues.Add(value);
            this.Parameters.Children.Add(name);
            this.Parameters.Children.Add(value);
        }
    }

    private void UpdateParameterValues(IEnumerable<string> newValues)
    {
        if (this.model == null)
        {
            return;
        }

        int row = 0;
        foreach (var value in newValues)
        {
            if (row >= this.parameterValues.Count)
            {
                break;
            }
            var textBlock = this.parameterValues[row];
            textBlock.Text = value;
            row++;
        }
    }
}

