using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PCMHammer.Viewmodels;
namespace PCMHammer.Views;
public partial class TuneEditorView:UserControl
{
    public TuneEditorView()
    {
        InitializeComponent();
        DataContextChanged+=(_,e)=>{if(e.OldValue is TuneEditorViewModel old)old.PropertyChanged-=ModelChanged;if(e.NewValue is TuneEditorViewModel model)model.PropertyChanged+=ModelChanged;BuildColumns();ApplyLayout();};
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(TuneEditorViewModel.Surface))BuildColumns();if(e.PropertyName==nameof(TuneEditorViewModel.SelectedLayout))ApplyLayout();}
    private void ApplyLayout()
    {
        string mode=(DataContext as TuneEditorViewModel)?.SelectedLayout??"Graph + table";
        bool graph=mode!="Table only",table=mode!="Graph only";
        TableViewTabs.Visibility=graph?Visibility.Visible:Visibility.Collapsed;
        NumericPanel.Visibility=table?Visibility.Visible:Visibility.Collapsed;
        GraphTableSplitter.Visibility=graph&&table?Visibility.Visible:Visibility.Collapsed;
        GraphRow.Height=graph?new GridLength(table?3:1,GridUnitType.Star):new GridLength(0);
        NumericRow.Height=table?new GridLength(graph?2:1,GridUnitType.Star):new GridLength(0);
        SplitRow.Height=new GridLength(graph&&table?7:0);
    }
    private bool restoringSelection;
    private void BuildColumns()
    {
        if(DataContext is not TuneEditorViewModel model||model.Surface is not SurfaceData surface){TableGrid.Columns.Clear();return;}
        var chosen=model.SelectedCellIndices.ToArray();
        restoringSelection=true;
        try
        {
            if(TableGrid.Columns.Count!=surface.Columns||TableGrid.Columns.Where((column,index)=>!Equals(column.Header,surface.ColumnLabels[index])).Any())
            {
                TableGrid.Columns.Clear();
                for(int i=0;i<surface.Columns;i++)
                {
                    var style=new Style(typeof(DataGridCell));
                    style.Setters.Add(new Setter(BackgroundProperty,new Binding($"Values[{i}].Fill")));
                    style.Setters.Add(new Setter(ToolTipProperty,$"{surface.ColumnAxis}: {surface.ColumnLabels[i]} • {surface.Units}"));
                    var selected=new Trigger {Property=DataGridCell.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(BorderBrushProperty,System.Windows.Media.Brushes.Black));selected.Setters.Add(new Setter(BorderThicknessProperty,new Thickness(2)));style.Triggers.Add(selected);
                    TableGrid.Columns.Add(new DataGridTextColumn {Header=surface.ColumnLabels[i],Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=46,MaxWidth=90,CellStyle=style,Binding=new Binding($"Values[{i}].Text") {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
                }
            }
            TableGrid.SelectedCells.Clear();
            foreach(int index in chosen)if(index/surface.Columns<model.TableRows.Count)
                TableGrid.SelectedCells.Add(new DataGridCellInfo(model.TableRows[index/surface.Columns],TableGrid.Columns[index%surface.Columns]));
        }
        finally{restoringSelection=false;}
    }
    private void TableGrid_SelectedCellsChanged(object sender,SelectedCellsChangedEventArgs e)
    {
        if(restoringSelection||DataContext is not TuneEditorViewModel model||model.IsRefreshing)return;
        model.SetSelection(TableGrid.SelectedCells.Select(cell=>TableGrid.Items.IndexOf(cell.Item)*model.ColumnCount+cell.Column.DisplayIndex));
    }
    private void SelectRow_Click(object sender,RoutedEventArgs e)
    {
        int row=TableGrid.Items.IndexOf(TableGrid.CurrentItem);if(row<0)return;
        TableGrid.SelectedCells.Clear();foreach(var column in TableGrid.Columns)TableGrid.SelectedCells.Add(new DataGridCellInfo(TableGrid.Items[row],column));
    }
    private void SelectColumn_Click(object sender,RoutedEventArgs e)
    {
        var column=TableGrid.CurrentCell.Column;if(column==null)return;
        TableGrid.SelectedCells.Clear();foreach(var row in TableGrid.Items)TableGrid.SelectedCells.Add(new DataGridCellInfo(row,column));
    }
    private void SelectAll_Click(object sender,RoutedEventArgs e)=>TableGrid.SelectAllCells();
    private void Copy_Click(object sender,RoutedEventArgs e)=>ApplicationCommands.Copy.Execute(null,TableGrid);
    private void Paste_Click(object sender,RoutedEventArgs e)=>PasteAtCursor();
    private void PasteAtCursor()
    {
        if(DataContext is not TuneEditorViewModel model||TableGrid.CurrentCell.Column==null)return;
        int row=TableGrid.Items.IndexOf(TableGrid.CurrentItem),column=TableGrid.CurrentCell.Column.DisplayIndex;
        TableGrid.CommitEdit(DataGridEditingUnit.Cell,true);
        try{model.PasteCells(row,column,Clipboard.GetText());}catch(Exception error){model.Message="Paste could not access the clipboard: "+error.Message;}
    }
    private void TableGrid_KeyDown(object sender,KeyEventArgs e)
    {
        if(DataContext is not TuneEditorViewModel model)return;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&(e.Key==Key.Z||e.Key==Key.Y)&&e.OriginalSource is not TextBox)
        {
            if(e.Key==Key.Z)model.Undo();else model.Redo();e.Handled=true;
        }
        else if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.Enter){TableGrid.CommitEdit(DataGridEditingUnit.Cell,true);model.CommitCells();e.Handled=true;}
        else if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.V&&TableGrid.CurrentCell.Column!=null)
        {
            PasteAtCursor();
            e.Handled=true;
        }
    }
}
