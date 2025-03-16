using System.Drawing.Text;
using Microsoft.UI.Dispatching;

namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingParametersPage : Page
{
    private record RowMetadata(TextBlock TextBlock, string Units);

    private List<RowMetadata> parameterMetadata = new();

    private DataLoggingParametersModel? model;

    private DispatcherQueue dispatcherQueue;

    public DataLoggingParametersPage()
    {
        this.dispatcherQueue = DispatcherQueue.GetForCurrentThread();
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

            this.model.LogProfile.ForEach((profileWrapper, ct) => this.InitializeParameters(profileWrapper.Profile)); 

            this.model.Rows.ForEach((row, ct) => this.UpdateParameterValues(row.Values));
        };
    }
    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        this.model?.StopLogging();
    }

    private ValueTask InitializeParameters(LogProfile profile)
    {
        if (this.model == null)
        {
            return ValueTask.CompletedTask;
        }

        // Operations that affect the UI need to run on the main thread.
        this.dispatcherQueue.TryEnqueue(() =>
        {
            int row = 0;
            foreach (var column in profile.Columns)
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
                parameterMetadata.Add(new RowMetadata(value, column.Conversion.Units));

                this.Parameters.RowDefinitions.Add(new RowDefinition());
                this.Parameters.Children.Add(name);
                this.Parameters.Children.Add(value);
            }
        });

        return ValueTask.CompletedTask;
    }

    private ValueTask UpdateParameterValues(IEnumerable<string> rowValues)
    {
        if (this.model == null)
        {
            return ValueTask.CompletedTask;
        }

        // Operations that affect the UI need to run on the main thread.
        this.dispatcherQueue.TryEnqueue(() =>
        {
            int row = 0;
            foreach (var value in rowValues)
            {
                if (row >= this.parameterMetadata.Count)
                {
                    break;
                }
                var rowMetadata = this.parameterMetadata[row];
                rowMetadata.TextBlock.Text = value + " " + rowMetadata.Units;
                row++;
            }
        });

        return ValueTask.CompletedTask;
    }
}

