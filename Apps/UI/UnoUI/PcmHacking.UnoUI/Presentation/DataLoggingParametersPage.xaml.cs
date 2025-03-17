using System.Data;
using System.Data.Common;
using System.Drawing.Text;
using Microsoft.UI.Dispatching;
using Uno.Extensions.Reactive;
using Windows.UI.ViewManagement;

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

            this.model.ErrorMessage.ForEach((value, ct) => this.ShowErrorMessage(value));

            // Let the Model know that it's safe to continue.
            this.model.InitializationEvent.Set();
            this.model.ProgressLogger.AddDebugMessage("DataLoggingParametersPage callbacks registered.");            
        };
    }
    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        this.model?.StopLogging();
    }

    private ValueTask InitializeParameters(Logger logger)
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
            
            foreach (ParameterGroup group in logger.DpidConfiguration.ParameterGroups)
            {
                foreach (LogColumn column in group.LogColumns)
                {
                    AddParameter(mainRowIndex, column.Parameter.Name, column.Conversion.Units, out TextBlock name, out TextBlock value);
                    mainRowIndex++;

                    this.Parameters.RowDefinitions.Add(new RowDefinition());
                    this.Parameters.Children.Add(name);
                    this.Parameters.Children.Add(value);

                    TextBlock? zoomValue = null;
                    if (column.Zoom)
                    {
                        StackPanel stackPanel;
                        AddZoomParameter(zoomRowIndex, column.Parameter.Name, column.Conversion.Units, out zoomValue, out stackPanel);
                        zoomRowIndex++;

                        this.ZoomedParameters.RowDefinitions.Add(new RowDefinition());
                        this.ZoomedParameters.Children.Add(stackPanel);
                    }

                    parameterMetadata.Add(new RowMetadata(value, zoomValue, column.Conversion.Units));
                }
            }

            foreach (LogColumn mathColumn in logger.MathValueProcessor.GetMathColumns())
            {
                AddParameter(mainRowIndex, mathColumn.Parameter.Name, mathColumn.Conversion.Units, out TextBlock name, out TextBlock value);
                mainRowIndex++;

                this.Parameters.RowDefinitions.Add(new RowDefinition());
                this.Parameters.Children.Add(name);
                this.Parameters.Children.Add(value);

                TextBlock? zoomValue = null;
                if (mathColumn.Zoom)
                {
                    StackPanel stackPanel;
                    AddZoomParameter(zoomRowIndex, mathColumn.Parameter.Name, mathColumn.Conversion.Units, out zoomValue, out stackPanel);
                    zoomRowIndex++;

                    this.ZoomedParameters.RowDefinitions.Add(new RowDefinition());
                    this.ZoomedParameters.Children.Add(stackPanel);
                }

                parameterMetadata.Add(new RowMetadata(value, zoomValue, mathColumn.Conversion.Units));
            }

            foreach (CanLogger.ParameterValue canParameter in logger.CanLogger.GetParameterValues())
            {
                AddParameter(mainRowIndex, canParameter.Name, canParameter.Units, out TextBlock name, out TextBlock value);
                mainRowIndex++;

                this.Parameters.RowDefinitions.Add(new RowDefinition());
                this.Parameters.Children.Add(name);
                this.Parameters.Children.Add(value);
                parameterMetadata.Add(new RowMetadata(value, null, canParameter.Units));
            }
        });

        return ValueTask.CompletedTask;
    }

    private static void AddParameter(int mainRowIndex, string name, string units, out TextBlock nameTextBlock, out TextBlock valueTextBlock)
    {
        nameTextBlock = new TextBlock();
        nameTextBlock.Text = name;
        nameTextBlock.Margin = new Thickness(5);
        nameTextBlock.HorizontalAlignment = HorizontalAlignment.Right;
        nameTextBlock.VerticalAlignment = VerticalAlignment.Center;
        nameTextBlock.SetValue(Grid.RowProperty, mainRowIndex);
        nameTextBlock.SetValue(Grid.ColumnProperty, 0);

        valueTextBlock = new TextBlock();
        valueTextBlock.Text = units;
        valueTextBlock.Margin = new Thickness(5);
        valueTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
        valueTextBlock.VerticalAlignment = VerticalAlignment.Center;
        valueTextBlock.SetValue(Grid.RowProperty, mainRowIndex);
        valueTextBlock.SetValue(Grid.ColumnProperty, 1);
    }

    private static void AddZoomParameter(int zoomRowIndex, string name, string units, out TextBlock? valueTextBlock, out StackPanel stackPanel)
    {
        stackPanel = new StackPanel();
        stackPanel.Orientation = Orientation.Vertical;
        stackPanel.VerticalAlignment = VerticalAlignment.Center;
        stackPanel.SetValue(Grid.RowProperty, zoomRowIndex);
        stackPanel.SetValue(Grid.ColumnProperty, 0);

        TextBlock zoomName = new TextBlock();
        zoomName.Margin = new Thickness(5);
        zoomName.HorizontalAlignment = HorizontalAlignment.Center;
        zoomName.VerticalAlignment = VerticalAlignment.Center;
        zoomName.Text = name;
        stackPanel.Children.Add(zoomName);

        valueTextBlock = new TextBlock();
        valueTextBlock.FontSize = 24;
        valueTextBlock.Margin = new Thickness(5);
        valueTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
        valueTextBlock.VerticalAlignment = VerticalAlignment.Center;
        valueTextBlock.Text = string.Empty;
        stackPanel.Children.Add(valueTextBlock);

        TextBlock zoomUnits = new TextBlock();
        zoomUnits.Margin = new Thickness(5);
        zoomUnits.HorizontalAlignment = HorizontalAlignment.Center;
        zoomUnits.VerticalAlignment = VerticalAlignment.Center;
        zoomUnits.Text = units;
        stackPanel.Children.Add(zoomUnits);
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

    private ValueTask ShowErrorMessage(string message)
    {
        this.dispatcherQueue.TryEnqueue(() =>
        {
            TextBlock errorTextBlock = new TextBlock();
            errorTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
            errorTextBlock.VerticalAlignment = VerticalAlignment.Center;
            errorTextBlock.Text = message;

            // Replace the page content with the error message;
            this.Content = errorTextBlock;
        });

        return ValueTask.CompletedTask;
    }
}

