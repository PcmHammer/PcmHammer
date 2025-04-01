using System.Data;
using System.Data.Common;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Shapes;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions.Reactive;
using Windows.UI.Text;
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

        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // This can't run in the constructor because the XamlRoot isn't available yet.
        bool darkMode = this.XamlRoot == null ? false : SystemThemeHelper.IsRootInDarkMode(this.XamlRoot);
        ColorUtilities.Initialize(darkMode);
    }

    private void OnDataContextChanged(object sender, DataContextChangedEventArgs e)
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
                    this.AddParameter(mainRowIndex, column.Parameter.Name, column.Conversion.Units, out TextBlock valueTextBlock);
                    mainRowIndex++;
                    
                    TextBlock? zoomValue = null;
                    if (column.Zoom)
                    {
                        this.AddZoomParameter(zoomRowIndex, column.Parameter.Name, column.Conversion.Units, out zoomValue);
                        zoomRowIndex++;
                    }

                    parameterMetadata.Add(new RowMetadata(valueTextBlock, zoomValue, column.Conversion.Units));
                }
            }

            foreach (LogColumn mathColumn in logger.MathValueProcessor.GetMathColumns())
            {
                this.AddParameter(mainRowIndex, mathColumn.Parameter.Name, mathColumn.Conversion.Units, out TextBlock valueTextBlock);
                mainRowIndex++;

                TextBlock? zoomValue = null;
                if (mathColumn.Zoom)
                {
                    this.AddZoomParameter(zoomRowIndex, mathColumn.Parameter.Name, mathColumn.Conversion.Units, out zoomValue);
                    zoomRowIndex++;
                }

                parameterMetadata.Add(new RowMetadata(valueTextBlock, zoomValue, mathColumn.Conversion.Units));
            }

            foreach (CanLogger.ParameterValue canParameter in logger.CanLogger.GetParameterValues())
            {
                this.AddParameter(mainRowIndex, canParameter.Name, canParameter.Units, out TextBlock valueTextBlock);
                mainRowIndex++;

                parameterMetadata.Add(new RowMetadata(valueTextBlock, null, canParameter.Units));
            }
        });

        return ValueTask.CompletedTask;
    }

    private void AddParameter(int mainRowIndex, string name, string units, out TextBlock valueTextBlock)
    {
        const int textSize = 30;
        
        TextBlock nameTextBlock = new TextBlock();
        nameTextBlock.Text = name;
        nameTextBlock.FontSize = textSize;
        nameTextBlock.Margin = new Thickness(5);
        nameTextBlock.HorizontalAlignment = HorizontalAlignment.Right;
        nameTextBlock.VerticalAlignment = VerticalAlignment.Center;
        
        Border nameBorder = new Border();
        nameBorder.Background = ColorUtilities.Instance.BackgroundBrushes[mainRowIndex % 2];
        nameBorder.SetValue(Grid.RowProperty, mainRowIndex);
        nameBorder.SetValue(Grid.ColumnProperty, 0);
        nameBorder.Child = nameTextBlock;

        valueTextBlock = new TextBlock();
        valueTextBlock.Text = units;
        valueTextBlock.FontSize = textSize;
        valueTextBlock.FontWeight = new FontWeight(700); // bold
        valueTextBlock.Margin = new Thickness(5);
        valueTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
        valueTextBlock.VerticalAlignment = VerticalAlignment.Center;

        Border valueBorder = new Border();
        valueBorder.Background = ColorUtilities.Instance.BackgroundBrushes[mainRowIndex % 2];
        valueBorder.SetValue(Grid.RowProperty, mainRowIndex);
        valueBorder.SetValue(Grid.ColumnProperty, 1);
        valueBorder.Child = valueTextBlock;

        this.Parameters.RowDefinitions.Add(new RowDefinition());
        this.Parameters.Children.Add(nameBorder);
        this.Parameters.Children.Add(valueBorder);
    }

    private void AddZoomParameter(int zoomRowIndex, string name, string units, out TextBlock? valueTextBlock)
    {
        const int labelSize = 30;
        const int valueSize = 60;
                
        StackPanel stackPanel = new StackPanel();
        stackPanel.Orientation = Orientation.Vertical;
        stackPanel.VerticalAlignment = VerticalAlignment.Center;

        Border border = new Border();
        border.Background = ColorUtilities.Instance.BackgroundBrushes[zoomRowIndex % 2];
        border.SetValue(Grid.RowProperty, zoomRowIndex);
        border.SetValue(Grid.ColumnProperty, 0);
        border.Child = stackPanel;

        TextBlock zoomName = new TextBlock();
        zoomName.Text = name;
        zoomName.FontSize = labelSize;
        zoomName.Margin = new Thickness(5);
        zoomName.HorizontalAlignment = HorizontalAlignment.Center;
        zoomName.VerticalAlignment = VerticalAlignment.Center;
        stackPanel.Children.Add(zoomName);

        valueTextBlock = new TextBlock();
        valueTextBlock.FontSize = valueSize;
        valueTextBlock.Margin = new Thickness(5);
        valueTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
        valueTextBlock.VerticalAlignment = VerticalAlignment.Center;
        valueTextBlock.Text = string.Empty;
        stackPanel.Children.Add(valueTextBlock);

        TextBlock zoomUnits = new TextBlock();
        zoomUnits.Text = units;
        zoomUnits.FontSize = labelSize;
        zoomUnits.Margin = new Thickness(5);
        zoomUnits.HorizontalAlignment = HorizontalAlignment.Center;
        zoomUnits.VerticalAlignment = VerticalAlignment.Center;
        stackPanel.Children.Add(zoomUnits);

        this.ZoomedParameters.RowDefinitions.Add(new RowDefinition());
        this.ZoomedParameters.Children.Add(border);
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

