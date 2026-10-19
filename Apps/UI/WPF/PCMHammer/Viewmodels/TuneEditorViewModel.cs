using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PcmHacking;
using PCMHammer.Views;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace PCMHammer.Viewmodels;
public partial class TuneCell : ObservableObject
{
    [ObservableProperty] private string text=string.Empty;
    public string Label {get;set;}=string.Empty;
    public Brush Fill {get;set;}=Brushes.White;
}

public sealed record TuneGridRow(string RowLabel, TuneCell[] Values);

public partial class TuneEditorViewModel : ObservableObject
{
    private readonly ILogger logger;
    private readonly Action<byte[]> apply;
    private readonly Action changed;
    private bool isReference;
    private bool referencePractice;
    private readonly List<string> xdfImportNotes=new();
    public bool IsReference=>isReference;
    private byte[]? image;
    private CalibrationEditor? editor;
    private bool refreshing;
    private string? appliedHash;
    private bool cellsDirty;
    private int[] selectedCellIndices=Array.Empty<int>();
    public bool IsRefreshing=>refreshing;
    public IReadOnlyList<int> SelectedCellIndices=>selectedCellIndices;
    public IReadOnlyList<string> SelectionTools {get;}=new[]{"Fill with value","Add / subtract","Multiply","Divide","Change by %","Interpolate across columns","Interpolate down rows","Interpolate surface","Smooth (%)","Restore original"};
    [ObservableProperty] private string selectedTool="Add / subtract";
    [ObservableProperty] private string toolAmount="1";
    [ObservableProperty] private string selectionStatus="No cells selected";
    public bool CanApplyTool=>CanEdit&&selectedCellIndices.Length>0;
    public string ToolHint=>SelectedTool switch {
        "Add / subtract"=>"Signed offset: 2 adds two units; -2 subtracts two units.",
        "Change by %"=>"10 increases selected values by 10%; -10 decreases them by 10%.",
        "Multiply"=>"1.10 increases values by 10%. Applies to selected cells only.",
        "Divide"=>"Divide selected values by this nonzero number.",
        "Smooth (%)"=>"0–100% strength. One pass using selected orthogonal neighbors only; this is not TunerPro's smoothing algorithm.",
        "Interpolate across columns"=>"Uses each selected row's two endpoints; a solid rectangle is required. Numeric axis labels control spacing.",
        "Interpolate down rows"=>"Uses each selected column's two endpoints; a solid rectangle is required. Numeric axis labels control spacing.",
        "Interpolate surface"=>"Bilinear interpolation between the four corners of a solid rectangle. Amount is ignored.",
        "Restore original"=>"Restore selected cells to the values when this definition was opened. Undo remains available; amount is ignored.",
        _=>"Replace every selected cell with the entered value. One operation, one undo step."};
    partial void OnSelectedToolChanged(string value)=>OnPropertyChanged(nameof(ToolHint));
    public void SetSelection(IEnumerable<int> indices)
    {
        selectedCellIndices=indices.Distinct().Where(i=>i>=0&&i<Cells.Count).OrderBy(i=>i).ToArray();
        SelectionStatus=selectedCellIndices.Length==0?"No cells selected":$"{selectedCellIndices.Length} selected";
        OnPropertyChanged(nameof(CanApplyTool));
    }
    [RelayCommand]
    public void ApplySelectionTool()
    {
        if(!CanApplyTool||editor==null||SelectedField==null)return;
        if(cellsDirty){Message="Save or cancel individual cell inputs before using a selection tool.";return;}
        try
        {
            int operation=Array.IndexOf(SelectionTools.ToArray(),SelectedTool);
            if(operation<0)throw new InvalidDataException("Choose a selection tool.");
            bool usesAmount=operation<=4||operation==8;
            double amount=usesAmount?double.Parse(ToolAmount,NumberStyles.Float,CultureInfo.InvariantCulture):0;
            var candidate=TableSelectionTools.Transform(editor.Read(SelectedField),SelectedField.Rows,SelectedField.Columns,selectedCellIndices,(TableTool)operation,amount,editor.ReadOriginal(SelectedField),NumericAxis(SelectedField.RowLabels),NumericAxis(SelectedField.ColumnLabels));
            string summary=$"{SelectedTool} {(usesAmount?ToolAmount:string.Empty)} · {selectedCellIndices.Length} selected cell(s)";
            editor.Edit(SelectedField,candidate,summary);LoadCells();RefreshJournal();
            Message=summary+". Updated in the editor; Undo reverses the whole operation. Nothing was written to the ECU.";
        }
        catch(Exception error){Message="Selection edit rejected: "+error.Message;}
    }
    private static double[]? NumericAxis(string[] labels)
    {
        if(labels.Length==0)return null;var values=new double[labels.Length];
        for(int i=0;i<labels.Length;i++)if(!double.TryParse(labels[i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]))return null;
        return values;
    }
    public ObservableCollection<CalibrationField> Fields {get;}=new();
    public ObservableCollection<TuneCell> Cells {get;}=new();
    public ObservableCollection<TuneGridRow> TableRows {get;}=new();
    [ObservableProperty] private SurfaceData? surface;
    public ObservableCollection<string> Journal {get;}=new();
    [ObservableProperty] private CalibrationField? selectedField;
    [ObservableProperty] private string message="Editing locked. Load your E38 tune and a trusted definition matching its OSID and OS fingerprint. No vehicle addresses are guessed.";
    [ObservableProperty] private string tuneIdentity="Avalanche LC9 · expected E38 · label service 12597121 / part 12605897 · OSID not read yet";
    [ObservableProperty] private int columnCount=1;
    [ObservableProperty] private IReadOnlyList<PlotPoint> curve=Array.Empty<PlotPoint>();
    [ObservableProperty] private bool isHostBusy;
    public bool CanExport=>editor!=null&&!IsDemo&&!IsHostBusy;
    public bool CanEdit=>(!isReference||referencePractice)&&editor!=null&&SelectedField!=null&&!IsHostBusy;
    public bool HasPendingChanges=>cellsDirty||(editor!=null&&editor.WorkingHash!=appliedHash);
    public bool IsDemo=>editor?.Definition.DemoOnly==true;
    public string FieldDescription=>SelectedField==null?"Open your tune and its matching definition, or try the demo spark table.":referencePractice?"Practice edits update the table and charts. Apply and BIN export are disabled. Table meanings and axis units are unverified.":SelectedField.Description;
    public string TableTitle=>SelectedField?.Name??"No table selected";
    public string AxisCaption=>SelectedField==null?string.Empty:$"Rows: {SelectedField.RowAxisLabel}  •  Columns: {SelectedField.ColumnAxisLabel}  •  Values: {SelectedField.Units}";
    public TuneEditorViewModel(ILogger logger,Action<byte[]> apply,Action changed){this.logger=logger;this.apply=apply;this.changed=changed;RefreshAreas();}
    partial void OnIsHostBusyChanged(bool value){OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(CanExport));OnPropertyChanged(nameof(CanApplyTool));}
    partial void OnSelectedFieldChanged(CalibrationField? value)
    {
        // Uncommitted input must not disappear on navigation. Keep the field until committed/discarded.
        if(cellsDirty&&!refreshing){refreshing=true;SelectedField=editor?.Definition.Fields.FirstOrDefault(f=>f.Id==activeFieldId);refreshing=false;Message="Commit or discard the cell inputs before switching tables.";return;}
        LoadCells();
    }
    private string? activeFieldId;
    public void BindTune(byte[]? bytes)
    {
        image=bytes==null?null:(byte[])bytes.Clone();editor=null;isReference=false;referencePractice=false;xdfImportNotes.Clear();appliedHash=null;cellsDirty=false;
        Fields.Clear();Cells.Clear();SelectedField=null;Curve=Array.Empty<PlotPoint>();Journal.Clear();
        if(image!=null){var validator=new FileValidator(image,logger);TuneIdentity=validator.IdentifyAndValidate()?"Loaded main image · OSID "+validator.GetOsidFromImage()+" · "+image.Length.ToString("N0")+" bytes":"Loaded image could not be validated.";}
        Message="Open BIN + XDF to view matching tables and practice edits. Vehicle edits require a trusted exact-OS native definition.";RefreshAreas();Notify();
    }
    [RelayCommand]
    public void LoadDefinition()
    {
        if(IsHostBusy||!ConfirmReplace())return;
        if(image==null){Message="Load the tune from your PCM first, or use the demonstration editor.";return;}
        var picker=new OpenFileDialog {Title="Load trusted PCM Hammer definition",Filter="PCM Hammer definition (*.htdef.json)|*.htdef.json"};
        if(picker.ShowDialog(Application.Current.MainWindow)!=true)return;
        try{LoadDefinitionObject(CalibrationDefinition.Load(File.ReadAllText(picker.FileName)));}
        catch(Exception error){Message="Definition rejected: "+error.Message;}
    }
    public void LoadDefinitionObject(CalibrationDefinition definition)
    {
        if(IsHostBusy||image==null)throw new InvalidOperationException("No editable tune is available.");
        var candidate=new CalibrationEditor(image,definition,logger);
        Attach(candidate);
        TuneIdentity="Loaded E38 main image · OSID "+definition.Osid+" · "+image.Length.ToString("N0")+" bytes";
        Message="Definition matched structurally and by OS fingerprint. Table meanings and limits are supplied by the definition author; verify that source before changing a vehicle tune.";
    }
    private bool ConfirmReplace()=>!HasPendingChanges||IsDemo||MessageBox.Show(Application.Current.MainWindow,"Discard the pending calibration edits and open another workspace?","Pending edits",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private void Attach(CalibrationEditor candidate,bool reference=false,bool practice=false)
    {
        refreshing=true;try{isReference=reference;referencePractice=practice;xdfImportNotes.Clear();editor=candidate;cellsDirty=false;appliedHash=editor.WorkingHash;Fields.Clear();foreach(var field in editor.Definition.Fields)Fields.Add(field);SelectedField=Fields.FirstOrDefault();}finally{refreshing=false;}
        RefreshAreas(true);LoadCells();RefreshJournal();Notify();
    }
    [RelayCommand]
    public void LoadDemo()
    {
        if(IsHostBusy||!ConfirmReplace())return;
        const int rows=9,columns=16;
        var data=new byte[1024];
        for(int r=0;r<rows;r++)for(int c=0;c<columns;c++)
        {
            // A synthetic shape for exploring the UI, not a recommended calibration.
            double example=8+25*(1-Math.Exp(-c/3.0))-r*2.1+2*Math.Sin(c/4.0);
            short raw=(short)Math.Round(example*10);int address=128+(r*columns+c)*2;
            data[address]=(byte)(raw>>8);data[address+1]=(byte)raw;
        }
        var definition=new CalibrationDefinition {Name="Demonstration only",DemoOnly=true,ImageSize=data.Length,
            Fields={new CalibrationField {Id="demo",Name="Spark advance — DEMO",Category="Spark",Address=128,Rows=rows,Columns=columns,Storage="s16be",Factor=0.1,Minimum=-20,Maximum=60,Units="degrees BTDC",RowAxisLabel="MAP (kPa)",ColumnAxisLabel="Engine speed (RPM)",
                RowLabels=Enumerable.Range(0,rows).Select(r=>(20+r*10).ToString()).ToArray(),ColumnLabels=Enumerable.Range(0,columns).Select(c=>(c*400).ToString()).ToArray(),
                Description="Ignition timing in degrees before top dead center. Find RPM across the top and MAP down the left. DEMO values are made up, not a tune for your engine."}}};
        Attach(new CalibrationEditor(data,definition,logger,true));
        Message="DEMO SPARK TABLE — made-up values for learning. Nothing here can be written to your truck.";TuneIdentity="Demonstration workspace · no vehicle calibration";
    }
    [RelayCommand]
    public void LoadE38Reference()
    {
        if(IsHostBusy||!ConfirmReplace())return;
        var data=new byte[33*4];
        for(int i=0;i<33;i++)
        {
            // Generated illustration: no vehicle calibration or external definition is bundled.
            var bytes=BitConverter.GetBytes((float)(i*i*0.5));
            if(BitConverter.IsLittleEndian)Array.Reverse(bytes);
            Array.Copy(bytes,0,data,i*4,4);
        }
        var definition=new CalibrationDefinition {Name="Synthetic read-only illustration",DemoOnly=true,ImageSize=data.Length,
            Fields={new CalibrationField {Id="illustration",Name="Synthetic curve � DEMO",Description="Generated values; no ECU mapping.",Category="Airflow",Rows=33,Columns=1,Storage="f32be",Minimum=0,Maximum=100000,Units="illustration",RowAxisLabel="Sample index",ColumnAxisLabel="Generated value",RowLabels=Enumerable.Range(0,33).Select(i=>i.ToString()).ToArray(),ColumnLabels=new[]{"Value"}}}};
        Attach(new CalibrationEditor(data,definition,logger,true),true);
        TuneIdentity="READ-ONLY SYNTHETIC ILLUSTRATION � no vehicle calibration";
        Message="Generated illustration only. Editing, Apply and Export are disabled. Your loaded tune is unchanged.";
    }
    private void LoadCells()
    {
        refreshing=true;
        try
        {
            bool fieldChanged=activeFieldId!=SelectedField?.Id;
            Cells.Clear();TableRows.Clear();cellsDirty=false;activeFieldId=SelectedField?.Id;
            if(fieldChanged)SetSelection(Array.Empty<int>());
            if(editor==null||SelectedField==null){Curve=Array.Empty<PlotPoint>();Surface=null;return;}
            ColumnCount=SelectedField.Columns;double[] values=editor.Read(SelectedField);double lo=values.Min(),hi=values.Max();
            string[] rows=SelectedField.RowLabels.Length==SelectedField.Rows?SelectedField.RowLabels:Enumerable.Range(1,SelectedField.Rows).Select(i=>i.ToString()).ToArray();
            string[] columns=SelectedField.ColumnLabels.Length==SelectedField.Columns?SelectedField.ColumnLabels:Enumerable.Range(1,SelectedField.Columns).Select(i=>i.ToString()).ToArray();
            for(int i=0;i<values.Length;i++)
            {
                double t=hi==lo?0.5:(values[i]-lo)/(hi-lo);
                Color heat=CalibrationSurface.Heat(t);
                var cell=new TuneCell {Text=values[i].ToString("0.####",CultureInfo.InvariantCulture),Label=$"r{i/ColumnCount+1} · c{i%ColumnCount+1}",Fill=new SolidColorBrush(Color.FromRgb((byte)((heat.R+255)/2),(byte)((heat.G+255)/2),(byte)((heat.B+255)/2)))};
                cell.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(TuneCell.Text)&&!refreshing){cellsDirty=true;Notify();}};Cells.Add(cell);
            }
            for(int row=0;row<SelectedField.Rows;row++)TableRows.Add(new TuneGridRow(rows[row],Cells.Skip(row*ColumnCount).Take(ColumnCount).ToArray()));
            Surface=new SurfaceData(SelectedField.Rows,ColumnCount,values,rows,columns,SelectedField.RowAxisLabel,SelectedField.ColumnAxisLabel,SelectedField.Units);
            Curve=(ColumnCount==1?values:values.Take(ColumnCount)).Select((v,i)=>new PlotPoint(i+1,v)).ToArray();
        }
        finally{refreshing=false;OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(FieldDescription));OnPropertyChanged(nameof(TableTitle));OnPropertyChanged(nameof(AxisCaption));Notify();}
    }
    public void PasteCells(int row,int column,string text)
    {
        if(!CanEdit||SelectedField==null)return;
        try
        {
            string[] lines=text.Replace("\r",string.Empty).TrimEnd('\n').Split('\n');
            var parsed=lines.Select(line=>line.Split('\t').Select(value=>double.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            if(row<0||column<0||row+parsed.Length>SelectedField.Rows||parsed.Any(line=>column+line.Length>ColumnCount)||parsed.Any(line=>line.Length!=parsed[0].Length))throw new InvalidDataException("Pasted cells do not fit this table.");
            if(parsed.SelectMany(v=>v).Any(v=>!double.IsFinite(v)||v<SelectedField.Minimum||v>SelectedField.Maximum))throw new InvalidDataException("Pasted values exceed the table limits.");
            for(int r=0;r<parsed.Length;r++)for(int c=0;c<parsed[r].Length;c++)Cells[(row+r)*ColumnCount+column+c].Text=parsed[r][c].ToString("0.####",CultureInfo.InvariantCulture);
            Message="Cells pasted. Choose Save table changes to validate them and update the graph.";
        }
        catch(Exception error){Message="Paste rejected: "+error.Message;}
    }
    [RelayCommand]
    public void CommitCells()
    {
        if(!CanEdit||editor==null||SelectedField==null)return;
        try
        {
            double[] values=Cells.Select(c=>double.Parse(c.Text,NumberStyles.Float,CultureInfo.InvariantCulture)).ToArray();
            editor.Edit(SelectedField,values);cellsDirty=false;LoadCells();RefreshJournal();
            Message=referencePractice?"Practice table updated. The chart reflects your edits. Apply and BIN export stay disabled; your original BIN is unchanged.":IsDemo?"Demo table updated. Try Undo / Redo. Nothing is sent to a vehicle.":"Edit staged in a private copy. Apply to the loaded tune or export an edited copy before flashing.";
        }
        catch(Exception error){Message="Edit rejected: "+error.Message;}
    }
    [RelayCommand] public void DiscardCells(){if(!IsHostBusy){LoadCells();Message=referencePractice?"Practice cell inputs canceled. Committed practice edits stay in this workspace; the original BIN is unchanged.":IsDemo?"DEMO EDITOR — synthetic data. Vehicle Apply and BIN Export are disabled.":"Cell inputs discarded. Previously committed table edits remain staged.";}}
    [RelayCommand] public void Undo(){if(IsHostBusy||editor==null)return;if(cellsDirty){LoadCells();Message="Unsaved cell inputs undone; committed table changes are unchanged.";return;}editor.Undo();LoadCells();RefreshJournal();Message="Undo complete. "+(referencePractice?"Practice changes only; the original BIN is unchanged.":IsDemo?"Demo data only.":"Apply the current editor state to update the loaded tune.");}
    [RelayCommand] public void Redo(){if(IsHostBusy||editor==null)return;if(cellsDirty){Message="Save or cancel cell inputs before Redo.";return;}editor.Redo();LoadCells();RefreshJournal();Message="Redo complete. "+(referencePractice?"Practice changes only; the original BIN is unchanged.":IsDemo?"Demo data only.":"Apply the current editor state to update the loaded tune.");}
    [RelayCommand]
    public void ApplyToTune()
    {
        if(IsHostBusy||editor==null||IsDemo)return;
        if(cellsDirty){Message="Commit the cell inputs first.";return;}
        try{byte[] exported=editor.Export();apply(exported);image=(byte[])exported.Clone();appliedHash=editor.WorkingHash;Message="Edited main image applied; checksums updated. Save a new copy and review it before any write.";Notify();}
        catch(Exception error){Message="Apply failed: "+error.Message;}
    }
    [RelayCommand]
    public void ExportEdited()
    {
        if(IsHostBusy||editor==null||IsDemo)return;
        if(cellsDirty){Message="Commit the cell inputs before export.";return;}
        var picker=new SaveFileDialog {Title="Export edited main image — use a new file",Filter="Main image (*.bin)|*.bin",FileName="PCM-Hammer-edited.bin",OverwritePrompt=true};
        if(picker.ShowDialog(Application.Current.MainWindow)!=true)return;
        try{byte[] exported=editor.Export();using(var output=new FileStream(picker.FileName,FileMode.CreateNew,FileAccess.Write))output.Write(exported);File.WriteAllText(picker.FileName+".changes.json",Newtonsoft.Json.JsonConvert.SerializeObject(editor.Changes,Newtonsoft.Json.Formatting.Indented));Message="Edited BIN and change journal exported. Existing files are never overwritten by this export.";}
        catch(Exception error){Message="Export failed: "+error.Message;}
    }
    [RelayCommand]
    public void InspectXdf()
    {
        var picker=new OpenFileDialog {Title="Inspect XDF (read-only)",Filter="XML XDF definition (*.xdf)|*.xdf"};
        if(picker.ShowDialog(Application.Current.MainWindow)!=true)return;
        try{using var reader=File.OpenText(picker.FileName);var items=XdfInspector.Read(reader);Message=$"XDF inspected: {items.Count} items. Native XDF editing and plug-ins are not supported in this version.";MessageBox.Show(string.Join(Environment.NewLine,items.Take(40))+Environment.NewLine+"Read-only inspection. No definition was activated.","XDF inspection");}
        catch(Exception error){Message="XDF inspection failed: "+error.Message;}
    }
    private void RefreshJournal(){Journal.Clear();foreach(var note in xdfImportNotes)Journal.Add(note);if(editor!=null)foreach(var entry in editor.Changes.TakeLast(100))Journal.Add(entry.TimeUtc.ToLocalTime().ToString("HH:mm:ss")+" · "+entry.Field+" · "+entry.Summary);Notify();}
    private void Notify(){OnPropertyChanged(nameof(HasPendingChanges));OnPropertyChanged(nameof(IsDemo));OnPropertyChanged(nameof(IsReference));OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(CanExport));OnPropertyChanged(nameof(CanApplyTool));changed();}
}
