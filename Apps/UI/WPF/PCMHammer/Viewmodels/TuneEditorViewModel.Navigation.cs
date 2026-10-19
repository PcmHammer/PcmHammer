using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using System.Collections.ObjectModel;

namespace PCMHammer.Viewmodels;
public sealed record TuningArea(string Key,string Name,int Count,string Description,string Needed)
{
    public string Status=>Count==0?"Definition needed":$"{Count} loaded item{(Count==1?"":"s")}";
}
public partial class TuneEditorViewModel
{
    public ObservableCollection<TuningArea> Areas {get;}=new();
    public ObservableCollection<CalibrationField> VisibleFields {get;}=new();
    [ObservableProperty] private TuningArea? selectedArea;
    [ObservableProperty] private string tableSearch=string.Empty;
    [ObservableProperty] private string selectedLayout="Graph only";
    public IReadOnlyList<string> WorkspaceLayouts {get;}=new[]{"Graph + table","Graph only","Table only"};
    public string AreaDescription=>SelectedArea?.Description??"Choose a tuning area.";
    public string AreaNeeded=>SelectedArea?.Needed??string.Empty;
    public string CoverageStatus=>referencePractice?$"{Fields.Count} defined entries · research · vehicle export locked":Fields.Count==0?"No definitions loaded":IsDemo?"Demonstration workspace":"Definition coverage · source verification required";
    private bool updatingAreas;
    private TuningArea? previousArea;
    private static readonly (string Key,string Name,string Description,string Needed)[] areaSpecs=new[]{
        ("all","All tables","Every table loaded in this workspace. Counts describe available definitions, not verified vehicle tuning coverage.","Load a matching BIN and XDF, or an independently checked native definition."),
        ("idle","Idle & startup","Starting, warm-up and idle control. The available entries depend on the loaded definition; their count does not establish complete tuning coverage.","Needed: verified desired idle speed, base/minimum airflow, cranking/startup airflow, idle spark control and confirmed physical axes."),
        ("airflow","Airflow","MAF and virtual VE determine the engine's air estimate. Charge-temperature candidates do not replace these models.","Needed: MAF curve/frequency breakpoints, virtual VE coefficients/model, operating-mode selection and confirmed axes/scaling."),
        ("fuel","Fueling","Fuel targets and enrichment. The example contains open-loop candidates with some missing physical axes.","Needed: verified enrichment targets/enablers/delays, closed-loop behavior, fuel-pressure assumptions and complete physical axes."),
        ("spark","Spark","Main timing maps and correction candidates. Labels, scaling and physical load-axis units need confirmation before vehicle use.","Confirm high/low octane maps, idle timing, knock response, temperature corrections and actual engine operating axes."),
        ("afm","AFM / DOD","Cylinder-deactivation calibration. A raw byte in a search result is not a verified disable procedure.","Confirm enable/disable encoding, dependent behavior and exact OS applicability; bench/vehicle test the mechanical-delete configuration."),
        ("torque","Torque & throttle","Engine torque modeling and electronic throttle control. No verified definitions for these controls are included.","Needed: requested/predicted torque models, throttle relationships and relevant limiters, supported by exact-OS evidence and tests."),
        ("injectors","Injector data","Injector characterization converts required fuel into injection time. No verified injector definitions are included.","Needed: flow versus pressure, voltage offset, short-pulse behavior and minimum pulse width, plus accurate data for the fitted injectors."),
        ("diagnostics","Diagnostics","Diagnostic configuration and protective behavior. Immobilizer OS patching is not implemented.","Needed: independently mapped diagnostic behavior and preserved engine protections. No unverified patch or blanket diagnostic changes are supplied."),
        ("other","Other tables","Definitions that do not match a tuning category.","Check the definition author and documentation before assigning functions to these fields.")};
    public static string AreaFor(CalibrationField field)
    {
        string category=field.Category.ToLowerInvariant();
        foreach(var spec in areaSpecs.Where(s=>s.Key!="all"))if(category==spec.Key||category==spec.Name.ToLowerInvariant())return spec.Key;
        string text=field.Name.ToLowerInvariant();
        if(text.Contains("injector"))return "injectors";
        if(text.Contains("immob")||text.Contains("vats")||text.Contains("remote start")||text.Contains("dtc"))return "diagnostics";
        if(text.Contains("spark")||text.Contains("octane")||text.Contains("dwell"))return "spark";
        if(text.Contains("afm")||text.Contains("dod")||text.Contains("deactivation"))return "afm";
        if(text.Contains("idle")||text.Contains("startup")||text.Contains("crank")||text.Contains("warm-up"))return "idle";
        if(text.Contains("torque")||text.Contains("throttle"))return "torque";
        if(text.Contains("maf")||text.Contains("airflow")||text.Contains("virtual ve")||text.Contains("charge temp")||text.Contains("speed/load"))return "airflow";
        if(text.Contains("fuel")||text.Contains("eq_r")||text.Contains("eqr"))return "fuel";
        return "other";
    }
    private void RefreshAreas(bool reset=false)
    {
        updatingAreas=true;
        try
        {
            string key=reset?"all":SelectedArea?.Key??"all";if(reset)TableSearch=string.Empty;Areas.Clear();
            foreach(var spec in areaSpecs)Areas.Add(new TuningArea(spec.Key,spec.Name,spec.Key=="all"?Fields.Count:Fields.Count(f=>AreaFor(f)==spec.Key),spec.Description,spec.Needed));
            SelectedArea=Areas.First(a=>a.Key==key);previousArea=SelectedArea;
        }
        finally{updatingAreas=false;}
        FilterFields();OnPropertyChanged(nameof(CoverageStatus));
    }
    partial void OnSelectedAreaChanged(TuningArea? value)
    {
        if(updatingAreas)return;
        if(cellsDirty){updatingAreas=true;SelectedArea=previousArea;updatingAreas=false;Message="Save or cancel the cell inputs before switching tuning areas.";return;}
        previousArea=value;FilterFields();
    }
    partial void OnTableSearchChanged(string value)
    {
        if(updatingAreas)return;
        if(cellsDirty){Message="Save or cancel cell inputs before filtering the table list.";return;}
        FilterFields();
    }
    private void FilterFields()
    {
        var current=SelectedField;
        var matches=Fields.Where(f=>(SelectedArea==null||SelectedArea.Key=="all"||AreaFor(f)==SelectedArea.Key)&&f.Name.Contains(TableSearch,StringComparison.OrdinalIgnoreCase)).ToArray();
        VisibleFields.Clear();foreach(var field in matches)VisibleFields.Add(field);
        SelectedField=current!=null&&matches.Contains(current)?current:matches.FirstOrDefault();
        OnPropertyChanged(nameof(AreaDescription));OnPropertyChanged(nameof(AreaNeeded));
    }
    [RelayCommand] public void ClearTableSearch()=>TableSearch=string.Empty;
    public string BuildCoverageReport()
    {
        var report=new System.Text.StringBuilder("# PCM Hammer definition coverage\n\n");
        report.AppendLine(TuneIdentity).AppendLine().AppendLine(CoverageStatus).AppendLine();
        report.AppendLine("Counts describe loaded definitions, not independently verified ECU behavior. No verified completeness claim is made.").AppendLine();
        foreach(var area in Areas.Where(a=>a.Key!="all"))
        {
            report.AppendLine("## "+area.Name).AppendLine().AppendLine(area.Status).AppendLine().AppendLine(area.Description).AppendLine().AppendLine(area.Needed).AppendLine();
            foreach(var field in Fields.Where(f=>AreaFor(f)==area.Key))report.AppendLine($"- {field.Name}: 0x{field.Address:X8}, {field.Rows}×{field.Columns}, {field.Storage}; factor {field.Factor.ToString(System.Globalization.CultureInfo.InvariantCulture)}, offset {field.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            report.AppendLine();
        }
        return report.ToString();
    }
    [RelayCommand] public void SaveCoverageReport()
    {
        if(IsHostBusy)return;
        var picker=new Microsoft.Win32.SaveFileDialog {Title="Save definition coverage report",Filter="Markdown report (*.md)|*.md",FileName="PCM-Hammer-definition-coverage.md"};
        if(picker.ShowDialog(System.Windows.Application.Current.MainWindow)!=true)return;
        try{System.IO.File.WriteAllText(picker.FileName,BuildCoverageReport());Message="Definition coverage report saved. No tune or ECU data was changed.";}
        catch(Exception error){Message="Coverage report could not be saved: "+error.Message;}
    }
}
