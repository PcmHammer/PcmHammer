using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PcmHacking;
using System.IO;
using System.Windows;

namespace PCMHammer.Viewmodels;

public partial class TuneEditorViewModel
{
    [RelayCommand]
    public void OpenXdfPair()
    {
        if(IsHostBusy||!ConfirmReplace())return;
        var bin=new OpenFileDialog {Title="Step 1 of 2 — open an E38 BIN",Filter="E38 binary (*.bin)|*.bin"};
        if(bin.ShowDialog(Application.Current.MainWindow)!=true)return;
        var xdf=new OpenFileDialog {Title="Step 2 of 2 — open the matching XDF",Filter="XDF definition (*.xdf)|*.xdf",InitialDirectory=Path.GetDirectoryName(bin.FileName)};
        if(xdf.ShowDialog(Application.Current.MainWindow)!=true)return;
        try
        {
            if(new FileInfo(bin.FileName).Length!=2097152)throw new InvalidDataException("Choose a complete 2 MB E38 BIN.");
            using var reader=File.OpenText(xdf.FileName);
            LoadXdfPair(File.ReadAllBytes(bin.FileName),reader,Path.GetFileName(bin.FileName),Path.GetFileName(xdf.FileName));
        }
        catch(Exception error){Message="BIN + XDF could not be opened: "+error.Message;}
    }

    public void LoadXdfPair(byte[] bytes,TextReader xdf,string name,string? definitionName=null)
    {
        if(IsHostBusy)throw new InvalidOperationException("Stop the active operation before opening a table workspace.");
        var imported=XdfPracticeReader.Read(bytes,xdf,logger);
        var candidate=new CalibrationEditor(bytes,imported.Definition,logger,true);
        Attach(candidate,true,true);
        TuneIdentity=$"REFERENCE PRACTICE · E38 OS {imported.Definition.Osid} · {name}"+(string.IsNullOrWhiteSpace(definitionName)?string.Empty:$" · {definitionName}");
        xdfImportNotes.AddRange(imported.Notes);RefreshJournal();
        Message=$"Showing {Fields.Count} entries from {definitionName??"the selected definition"}. Opening a definition replaces the previous table list; other settings in the BIN may still be undefined. Practice edits stay in memory; vehicle Apply and BIN export are disabled. See Import notes for skipped entries.";
    }

    [RelayCommand]
    public void LoadSparkExample()
    {
        if(IsHostBusy||!ConfirmReplace())return;
        LoadDemo();
    }
}
