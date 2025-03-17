using System.Drawing.Text;
using Microsoft.UI.Dispatching;
using Uno.Extensions.Reactive;

namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingParametersPage : Page
{
    private record RowMetadata(TextBlock Value, TextBlock? ZoomedValue, string Units);

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

            this.model.ProgressLogger.AddDebugMessage("DataLoggingParametersPage DataContext set.");

            this.model.LogProfile.ForEach((loggerWrapper, ct) => this.InitializeParameters(loggerWrapper.Logger)); 

            this.model.Rows.ForEach((row, ct) => this.UpdateParameterValues(row.Values));

            // Let the Model know that it's safe to continue.
            this.model.InitializationEvent.Set();
            this.model.ProgressLogger.AddDebugMessage("DataLoggingParametersPage callbacks registered.");            
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

        this.model.ProgressLogger.AddDebugMessage("Initializing parameter grid.");

        // Operations that affect the UI need to run on the main thread.
        this.dispatcherQueue.TryEnqueue(() =>
        {
            this.Parameters.RowDefinitions.Clear();
            this.ZoomedParameters.RowDefinitions.Clear();
            this.Parameters.Children.Clear();
            this.ZoomedParameters.Children.Clear();

            int mainRowIndex = 0;
            int zoomRowIndex = 0;
            foreach (var column in profile.Columns)
            {
                TextBlock name = new TextBlock();
                name.Text = column.Parameter.Name;
                name.Margin = new Thickness(5);
                name.HorizontalAlignment = HorizontalAlignment.Right;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.SetValue(Grid.RowProperty, mainRowIndex);
                name.SetValue(Grid.ColumnProperty, 0);

                TextBlock value = new TextBlock();
                value.Text = column.Conversion.Units;
                value.Margin = new Thickness(5);
                value.HorizontalAlignment = HorizontalAlignment.Left;
                value.VerticalAlignment = VerticalAlignment.Center;
                value.SetValue(Grid.RowProperty, mainRowIndex);
                value.SetValue(Grid.ColumnProperty, 1);

                TextBlock? zoomValue = null;
                if (column.Zoom)
                {
                    StackPanel stackPanel = new StackPanel();
                    stackPanel.Orientation = Orientation.Vertical;
                    stackPanel.VerticalAlignment = VerticalAlignment.Center;
                    stackPanel.SetValue(Grid.RowProperty, zoomRowIndex);
                    stackPanel.SetValue(Grid.ColumnProperty, 0);

                    TextBlock zoomName = new TextBlock();
                    zoomName.Margin = new Thickness(5);
                    zoomName.HorizontalAlignment = HorizontalAlignment.Center;
                    zoomName.VerticalAlignment = VerticalAlignment.Center;
                    zoomName.Text = column.Parameter.Name;
                    stackPanel.Children.Add(zoomName);

                    zoomValue = new TextBlock();
                    zoomValue.FontSize = 24;
                    zoomValue.Margin = new Thickness(5);
                    zoomValue.HorizontalAlignment = HorizontalAlignment.Center;
                    zoomValue.VerticalAlignment = VerticalAlignment.Center;
                    zoomValue.Text = string.Empty;
                    stackPanel.Children.Add(zoomValue);

                    TextBlock zoomUnits = new TextBlock();
                    zoomUnits.Margin = new Thickness(5);
                    zoomUnits.HorizontalAlignment = HorizontalAlignment.Center;
                    zoomUnits.VerticalAlignment = VerticalAlignment.Center;
                    zoomUnits.Text = column.Conversion.Units;
                    stackPanel.Children.Add(zoomUnits);

                    zoomRowIndex++;
                    this.ZoomedParameters.RowDefinitions.Add(new RowDefinition());
                    this.ZoomedParameters.Children.Add(stackPanel);

                }

                mainRowIndex++;
                parameterMetadata.Add(new RowMetadata(value, zoomValue, column.Conversion.Units));

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

        if (this.parameterMetadata.Count == 0)
        {
            this.model.ProgressLogger.AddDebugMessage("Parameter grid not initialized.");
            return ValueTask.CompletedTask;
        }

        // Operations that affect the UI need to run on the main thread.
        this.dispatcherQueue.TryEnqueue(() =>
        {
            int column = 0;
            foreach (var value in rowValues)
            {
                if (column >= this.parameterMetadata.Count)
                {
                    break;
                }
                var rowMetadata = this.parameterMetadata[column];
                rowMetadata.Value.Text = value + " " + rowMetadata.Units;
                if (rowMetadata.ZoomedValue?.Text != null)
                {
                    rowMetadata.ZoomedValue.Text = value;
                }

                column++;
            }
        });

        return ValueTask.CompletedTask;
    }
}

