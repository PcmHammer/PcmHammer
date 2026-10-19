using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using PCMHammer.Viewmodels;
using PcmHacking;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int code=1;
        var app=new Application();
        XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var appXml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"UI-check-App.xaml"));
        var dictionary=new XElement(appXml.Root!.Element(presentation+"Application.Resources")!.Elements().Single());
        dictionary.SetAttributeValue(XNamespace.Xmlns+"helpers","clr-namespace:PCMHammer.Helpers;assembly=PCMHammer");
        dictionary.SetAttributeValue(XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml");
        string resourceXaml=dictionary.ToString().Replace("clr-namespace:PCMHammer.Helpers\"", "clr-namespace:PCMHammer.Helpers;assembly=PCMHammer\"");
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resourceXaml);
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        string folder=args.Length==0 ? AppContext.BaseDirectory : args[0];
        Directory.CreateDirectory(folder);
        app.Dispatcher.InvokeAsync(async ()=>
        {
            try
            {
                var window=new PCMHammer.MainWindow();
                app.MainWindow=window;
                window.ShowInTaskbar=false;window.ShowActivated=false;
                window.Left=-20000;window.Top=-20000;window.Width=1360;window.Height=900;
                window.Show();
                var vm=(MainWindowViewModel)window.DataContext;
                if(args.Length>=3&&args[1]!="-"&&args[2]!="-")
                {
                    byte[] referenceBytes=File.ReadAllBytes(args[1]);var referenceValidator=new FileValidator(referenceBytes,new QuietLogger());
                    bool valid=referenceValidator.IdentifyAndValidate();
                    using var xdf=File.OpenText(args[2]);var entries=XdfInspector.Read(xdf);
                    File.WriteAllText(Path.Combine(folder,"reference-inspection.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {Valid=valid,Osid=referenceValidator.GetOsidFromImage(),Type=referenceValidator.GetFileType().ToString(),Entries=entries},Newtonsoft.Json.Formatting.Indented));
                    Check(valid&&referenceValidator.GetOsidFromImage()==12612381,"Downloaded reference BIN validates as E38 OS 12612381");
                }
                var logging=vm.DataLogging;
                Check(vm.AdapterStatus=="No OBD adapter connected"&&!vm.CheckAdapterConnectionCommand.CanExecute(null),"Footer starts disconnected without pretending that an adapter is present");
                Check(vm.WindowTitle.StartsWith("PCM Hammer"),"New product title is shown");
                var logo=Find<System.Windows.Controls.Image>(window).Single(i=>i.Name=="BrandMark");
                Check(logo.Source is BitmapSource brand&&brand.PixelWidth>0&&window.Icon!=null,"Brand mark and Windows icon are deployed and readable");
                Check(!vm.TuneEditor.CanEdit&&!vm.TuneEditor.CanExport,"Real editor starts locked without a tune and exact definition");
                logging.SelectedProfile="Stock baseline";
                Check(logging.Channels.Count(c=>c.Selected)==12,"Baseline profile chooses twelve readings");
                logging.SelectedProfile="Misfire investigation";
                Check(logging.Channels.Count(c=>c.Selected)==12,"Misfire profile includes all eight cylinders");
                logging.SelectedProfile="Cam idle & startup";
                Check(logging.Channels.Count(c=>c.Selected)==12,"Cam idle profile is available");
                Check(logging.Channels.Count>=15,"Parameter definitions are deployed and loaded");
                var speedChannel=logging.Channels.Single(c=>c.Parameter.Id=="VehicleSpeedSensor");
                double milesPerHour=new DynamicExpresso.Interpreter().Eval<double>(speedChannel.Parameter.Conversions.Single(c=>c.Units=="mph").Expression,new DynamicExpresso.Parameter("x",100d));
                Check(Math.Abs(milesPerHour-62.1371192237334)<1e-9,"Recorded speed uses the correct km/h-to-mph conversion");
                logging.ApplyGaugeSample(new[]{new LogRowElement("EngineSpeed","Engine Speed","RPM","2400",2400),new LogRowElement("VehicleSpeedSensor","Speed","kph","100",100)},false);
                Check(logging.GaugeRpm==2400&&Math.Abs(logging.GaugeSpeed-62.1371192237334)<1e-9,"Tachometer and mph speedometer consume numeric samples with correct units");
                logging.GaugeSpeedUnits="km/h";Check(logging.GaugeSpeed==100&&logging.GaugeSpeedMaximum==260,"Speedometer switches units without re-polling the vehicle");logging.GaugeSpeedUnits="mph";
                logging.ApplyGaugeSample(new[]{new LogRowElement("VehicleSpeedSensor","Speed","kph","Unavailable",double.NaN)},false);
                Check(double.IsNaN(logging.GaugeSpeed)&&double.IsNaN(logging.GaugeRpm),"Missing or unsupported gauge data does not appear as zero");
                Check(!logging.StartStopCommand.CanExecute(null),"Vehicle logging disabled without an adapter");
                using var device=new MockDevice(null!,new QuietLogger());
                vm.SelectedDevice=device;
                Check(vm.AdapterStatus.Contains("unverified"),"Selecting a device alone does not claim a verified connection");
                var picker=new DevicePickerViewModel(new QuietLogger());
                var failed=new SimulatedAdapter {Initializes=false};await picker.InitializeCandidateAsync(failed,"Simulated failed adapter");
                Check(picker.SelectedDevice==null&&failed.Disposed,"A failed initialization cannot be accepted and releases its device");
                var delayed=new SimulatedAdapter {Pending=new TaskCompletionSource<bool>()};await picker.InitializeCandidateAsync(delayed,"Simulated delayed adapter",TimeSpan.FromMilliseconds(20));
                Check(picker.SelectedDevice==null&&!delayed.Disposed,"Timed-out initialization is not accepted or disposed while native work is pending");
                delayed.Pending.SetResult(true);await Task.Delay(40);Check(delayed.Disposed&&picker.SelectedDevice==null,"Timed-out device is released after its pending request completes");
                using(var simulated=new SimulatedAdapter())
                {
                    await picker.InitializeCandidateAsync(simulated,"Simulated CAN adapter");Check(ReferenceEquals(picker.SelectedDevice,simulated),"Successful initialization is accepted");
                    vm.SelectedDevice=simulated;await vm.CheckAdapterConnection();
                    Check(vm.AdapterStatus.Contains("CAN available"),"Connected CAN-capable adapter appears in the footer");
                    vm.IsBusMonitorRunning=true;Check(!vm.CheckAdapterConnectionCommand.CanExecute(null),"Connection checks cannot interfere with monitoring");vm.IsBusMonitorRunning=false;
                    simulated.SupportsCan=false;await vm.CheckAdapterConnection();Check(vm.AdapterStatus.Contains("no E38 CAN support"),"Connected VPW-only adapter is distinguished from E38 compatibility");
                    simulated.Responds=false;await vm.CheckAdapterConnection();Check(vm.AdapterStatus=="Adapter not responding","Failed connection recheck changes the footer status");
                }
                vm.SelectedDevice=device;
                Check(vm.ReadPCMCommand.CanExecute(null),"PCM operation enabled before logging");
                logging.DemoMode=true;
                Check(logging.StartStopCommand.CanExecute(null),"Demo does not require an adapter");
                vm.IsOperationRunning=true;
                Check(!logging.StartStopCommand.CanExecute(null),"Flashing excludes data logging");
                vm.IsOperationRunning=false;
                vm.IsBusMonitorRunning=true;
                Check(!logging.StartStopCommand.CanExecute(null),"Bus monitor excludes data logging");
                vm.IsBusMonitorRunning=false;

                string path=Path.Combine(folder,"DEMO-integration-check.csv");
                logging.SelectedProfile="Stock baseline";
                var columns=logging.Channels.Where(c=>c.Selected).Select(c=>c.Column()).ToArray();
                Task recording=logging.RecordTo(path,columns,true);
                Check(vm.IsDataLoggingRunning,"Logging immediately claims the device");
                Check(!vm.ReadPCMCommand.CanExecute(null) && !vm.LoadFileCommand.CanExecute(null),"PCM reads and device/file changes locked during logging");
                Check(vm.BusMonitor.IsHostBusy,"Bus monitor locked during logging");
                Check(vm.TuneEditor.IsHostBusy,"Tune editing is locked while recording");
                for(int attempt=0;attempt<100 && logging.SampleCount<3;attempt++) await Task.Delay(50);
                Check(logging.SampleCount>=3,"Live data samples reach the UI");
                Check(logging.Channels.Any(c=>c.Value!="—"),"Live readings update");
                Check(logging.ChartPoints.Count>=3,"Live samples populate the chart");
                Check(double.IsFinite(logging.GaugeSpeed)&&double.IsFinite(logging.GaugeRpm)&&logging.GaugeStatus.Contains("DEMO"),"Both gauges update from the recording stream and visibly identify demo data");
                logging.ExpireGaugeSample(DateTime.UtcNow.AddSeconds(4));
                Check(double.IsNaN(logging.GaugeSpeed)&&double.IsNaN(logging.GaugeRpm),"Stale gauge data clears instead of holding a misleading reading");
                long expiredAt=logging.SampleCount;for(int attempt=0;attempt<100&&logging.SampleCount==expiredAt;attempt++)await Task.Delay(30);
                using (var liveFile=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                using (var liveReader=new StreamReader(liveFile))
                    Check(liveReader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Length>=4,"Rows are flushed while recording");

                vm.SelectedTabIndex=5;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                var instruments=Find<PCMHammer.Views.InstrumentGauge>(window).ToArray();
                Check(instruments.Length==2&&instruments.All(g=>g.ActualHeight>200&&g.ActualWidth>250),"Speedometer and tachometer render as readable vector instruments");
                var primaryNavigation=Find<System.Windows.Controls.Menu>(window).Single(m=>m.Name=="PrimaryNavigation");
                Check(primaryNavigation.ActualHeight<=35,"Commands and workspace navigation share one compact row");
                var fileMenu=Find<System.Windows.Controls.MenuItem>(window).Single(m=>m.Header?.ToString()=="_File");
                var menuLabel=(System.Windows.Controls.ContentPresenter)fileMenu.Template.FindName("TopMenuLabel",fileMenu);
                Check(menuLabel.ActualHeight>10&&menuLabel.TranslatePoint(new Point(),fileMenu).Y+menuLabel.ActualHeight<=fileMenu.ActualHeight,"Compact menu labels are visible inside the navigation row");
                fileMenu.IsSubmenuOpen=true;await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                var filePopup=(System.Windows.Controls.Primitives.Popup)fileMenu.Template.FindName("PART_Popup",fileMenu);
                Check(filePopup.IsOpen&&filePopup.Child is FrameworkElement popupContent&&popupContent.ActualWidth>100,"The compact File menu still opens its commands");fileMenu.IsSubmenuOpen=false;
                Capture(window,Path.Combine(folder,"logging-preview.png"));
                window.Width=1180;window.Height=780;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Check(instruments.All(g=>g.ActualWidth>250)&&Find<System.Windows.Controls.ItemsControl>(window).Any(c=>ReferenceEquals(c.ItemsSource,logging.Channels)&&c.ActualHeight>100),"Gauges and channel readings fit at the minimum supported window size");
                window.Width=1360;window.Height=900;

                await logging.StopAsync();await recording;
                Check(!vm.IsDataLoggingRunning && vm.IsDeviceControlEnabled,"Stop releases device after the worker ends");
                Check(vm.ReadPCMCommand.CanExecute(null),"PCM operation re-enabled after logging");
                Check(File.ReadAllLines(path).Skip(1).All(l=>l.StartsWith("\"DEMO\",")),"Every demo CSV sample labeled DEMO");
                logging.DemoMode=false;
                Check(!logging.StartStopCommand.CanExecute(null),"Real logging still requires a vehicle");
                using(var csv=File.OpenText(path))logging.LoadRecording(RecordingCsvReader.Read(csv));
                Check(logging.ChartStatus.Contains("DEMO")&&logging.ChartPoints.Count>=3,"Saved CSV reopens into a chart with its demo label");
                Check(double.IsNaN(logging.GaugeSpeed)&&double.IsNaN(logging.GaugeRpm)&&logging.GaugeStatus.Contains("review"),"Opening a saved chart does not present old data as live gauges");
                var tune=vm.TuneEditor;
                tune.LoadDemo();tune.SelectedLayout="Graph + table";
                Check(tune.CanEdit&&!tune.CanExport&&tune.IsDemo,"Demo grid editable with export and apply disabled");
                string demoOriginal=tune.Cells[0].Text;
                Check(tune.Surface?.Rows==9&&tune.Surface.Columns==16&&tune.Surface.ColumnLabels[1]=="400"&&tune.Surface.RowLabels[0]=="20","Spark table has RPM and MAP axes and a complete 3D surface");
                tune.Cells[0].Text="9";tune.CommitCells();
                Check(tune.Cells[0].Text=="9"&&tune.Journal.Count>0,"Demo edits populate the change journal");
                tune.Undo();Check(tune.Cells[0].Text==demoOriginal,"Editor Undo restores prior value");
                tune.Redo();Check(tune.Cells[0].Text=="9","Editor Redo restores staged value");
                tune.Cells[0].Text="NaN";tune.CommitCells();Check(tune.Message.StartsWith("Edit rejected"),"Editor rejects nonnumeric or nonfinite cell input");tune.DiscardCells();
                tune.PasteCells(0,0,"9\t10\n7\t8");tune.CommitCells();
                Check(tune.Surface!.Values[0]==9&&tune.Surface.Values[17]==8,"Rectangular paste updates table and surface together");
                tune.PasteCells(0,0,"12\t10000");Check(tune.Message.StartsWith("Paste rejected")&&tune.Cells[0].Text=="9","Invalid paste is rejected without partial edits");
                tune.PasteCells(8,15,"1\t2");Check(tune.Message.StartsWith("Paste rejected"),"Paste cannot spill outside the table");
                tune.DiscardCells();
                vm.SelectedTabIndex=6;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                var grid=Find<System.Windows.Controls.DataGrid>(window).First(g=>g.Name=="TableGrid");
                var history=Find<System.Windows.Controls.Expander>(window).Single(e=>e.Header?.ToString()=="Notes & edit history");
                Check(!history.IsExpanded&&history.ActualHeight<=23,"Import notes and edit history collapse to a compact disclosure");
                Check(grid.ActualHeight>=120&&grid.Columns.Count==16,"Numeric table has a dedicated readable view with sixteen RPM columns");
                var beforeBulk=tune.Surface!.Values.ToArray();
                foreach(int index in new[]{0,1,16,17})grid.SelectedCells.Add(new System.Windows.Controls.DataGridCellInfo(tune.TableRows[index/16],grid.Columns[index%16]));
                Check(tune.SelectedCellIndices.Count==4,"Actual grid selection reaches the bulk edit tools");
                tune.SelectedTool="Add / subtract";tune.ToolAmount="2";tune.ApplySelectionTool();
                Check(tune.Surface.Values[0]==beforeBulk[0]+2&&tune.Surface.Values[17]==beforeBulk[17]+2&&tune.Surface.Values[2]==beforeBulk[2],"Bulk offset changes selected cells only and updates the surface");
                Check(grid.SelectedCells.Count==4&&tune.SelectedCellIndices.Count==4,"Selected range survives an applied edit");
                tune.Undo();Check(tune.Surface.Values.SequenceEqual(beforeBulk),"One Undo reverses the whole selected-cell operation");
                tune.Redo();int journalCount=tune.Journal.Count;tune.ToolAmount="1000";tune.ApplySelectionTool();
                Check(tune.Message.StartsWith("Selection edit rejected")&&tune.Journal.Count==journalCount,"Out-of-range bulk edit adds no partial changes or history");
                tune.Undo();tune.ToolAmount="1";
                Capture(window,Path.Combine(folder,"editor-preview.png"));
                var tableTabs=Find<System.Windows.Controls.TabControl>(window).Single(t=>t.Name=="TableViewTabs");
                tableTabs.SelectedIndex=0;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Check(Find<PCMHammer.Views.CalibrationSurface>(window).Single().ActualHeight>=150,"3D surface has a dedicated view of the same calibration data");
                Capture(window,Path.Combine(folder,"surface-preview.png"));
                tableTabs.SelectedIndex=0;
                // Synthetic mapping on the upstream fixture, for offline testing only. Never shipped as a tuning definition.
                byte[] stock=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"TestData","E38_2048KiB_12607218.bin"));
                vm.SelectedTabIndex=0;
                vm.LoadedPackage=new PcmPackage {Controllers={new PackageController {ModuleType="E38",Images={new PackageImage {Target="main",Data=stock}}}}};
                Check(vm.SelectedTabIndex==6&&tune.TuneIdentity.Contains("12607218"),"A newly read or loaded tune opens automatically in the editor");
                var segments=FileValidator.GetE38MasterSegments(stock);var os=segments.First(s=>s.BlockType==BlockType.OperatingSystem);var cal=segments.First(s=>s.BlockType==BlockType.Calibration);
                tune.LoadDefinitionObject(new CalibrationDefinition {Name="TEST ONLY",Osid=12607218,ImageSize=stock.Length,OsSha256=CalibrationEditor.Hash(stock,os.Start,os.Length),Fields={new CalibrationField {Id="test",Address=cal.Start+0x40,Minimum=0,Maximum=65535}}});
                Check(tune.CanExport&&vm.CanWriteDocument,"Matched test definition unlocks the native editor");
                double oldValue=double.Parse(tune.Cells[0].Text,System.Globalization.CultureInfo.InvariantCulture);
                tune.Cells[0].Text=((oldValue+1)%65536).ToString(System.Globalization.CultureInfo.InvariantCulture);tune.CommitCells();
                Check(!vm.WritePCMCommand.CanExecute(null)&&!vm.SaveFileCommand.CanExecute(null),"Unapplied changes prevent flashing or saving stale main image");
                tune.ApplyToTune();Check(vm.DocumentDirty&&vm.CanWriteDocument&&!tune.HasPendingChanges,"Apply updates loaded image and marks it unsaved");
                tune.Undo();Check(tune.HasPendingChanges&&!vm.CanWriteDocument,"Undo after Apply requires applying the restored value");
                tune.ApplyToTune();Check(!tune.HasPendingChanges&&vm.CanWriteDocument,"Restored value can be applied without leaving stale edits");
                var beforeReference=vm.LoadedPackage!.Controllers[0].Image("main")!.Data!.ToArray();
                tune.LoadE38Reference();
                Check(tune.IsReference&&!tune.CanEdit&&!tune.CanExport&&tune.Cells.Count==33&&tune.Curve.Count==33,"Generated read-only reference curve is available in an isolated read-only view with a complete 2D curve");
                Check(grid.IsReadOnly&&grid.IsEnabled,"Reference grid remains scrollable and copyable while editing is blocked");
                Check(vm.LoadedPackage.Controllers[0].Image("main")!.Data!.SequenceEqual(beforeReference)&&!vm.CanWriteDocument,"Viewing the generated reference preserves the loaded tune and blocks programming from the reference view");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Capture(window,Path.Combine(folder,"reference-preview.png"));
                tune.LoadSparkExample();
                Check(tune.IsDemo&&!tune.CanExport&&tune.TuneIdentity.Contains("Demonstration"),"Public Example opens generated data without a borrowed BIN or definition");
                var fixture=PracticeFixture(stock);
                using(var fixtureReader=new StringReader(fixture.Xdf))tune.LoadXdfPair(fixture.Image,fixtureReader,"generated test mapping","synthetic UI fixture");
                Check(tune.Fields.Count==5&&tune.Cells.Count==1089&&tune.TableRows.Count==33&&tune.ColumnCount==33,"Synthetic fixture opens five distinct practice entries and starts on the main spark map");
                Check(tune.TuneIdentity.Contains("12607218")&&tune.CanEdit&&tune.IsReference&&tune.IsDemo&&!tune.CanExport&&!grid.IsReadOnly,"XDF example enables practice cell editing without enabling vehicle Apply or BIN export");
                Check(tune.Surface?.Values.Length==1089&&tune.Curve.Count==33,"Imported spark data reaches the 3D surface and first-row curve");
                var high=tune.SelectedField;var highOriginal=tune.Cells[0].Text;
                tune.SetSelection(new[]{0,1,33,34});tune.SelectedTool="Add / subtract";tune.ToolAmount="1";tune.ApplySelectionTool();
                Check(tune.Cells[0].Text!=highOriginal&&tune.Journal.Any(n=>n.Contains("Names, axes")),"Bulk practice edits update imported cells while preserving import notes");
                tune.Undo();Check(tune.Cells[0].Text==highOriginal,"Imported spark practice edits undo correctly");
                tune.SelectedField=tune.Fields[1];Check(tune.Cells.Count==1089&&tune.TableTitle.Contains("Low-octane"),"Selecting low-octane opens its distinct spark table");
                tune.SelectedField=high;tableTabs.SelectedIndex=0;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Capture(window,Path.Combine(folder,"xdf-spark-surface.png"));
                tableTabs.SelectedIndex=0;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Capture(window,Path.Combine(folder,"xdf-spark-table.png"));
                tune.Cells[0].Text="31";tune.CommitCells();tune.ApplyToTune();
                Check(vm.LoadedPackage.Controllers[0].Image("main")!.Data!.SequenceEqual(beforeReference)&&!vm.CanWriteDocument,"Committing and attempting Apply in XDF practice cannot alter or program the loaded vehicle tune");
                using(var mismatched=new StringReader("<XDFFORMAT><XDFHEADER><deftitle>12612381</deftitle></XDFHEADER></XDFFORMAT>"))
                {
                    bool rejected=false;try{tune.LoadXdfPair(stock,mismatched,"wrong OS");}catch(InvalidDataException){rejected=true;}
                    Check(rejected&&tune.Fields.Count==5&&tune.TuneIdentity.Contains("12607218"),"A mismatched pair is rejected without clearing the current XDF workspace");
                }
                Check(tune.Areas.Single(a=>a.Key=="spark").Count==2&&tune.Areas.Single(a=>a.Key=="fuel").Count==1&&tune.Areas.Single(a=>a.Key=="idle").Count==1&&tune.Areas.Single(a=>a.Key=="airflow").Count==1,"Tuning areas report actual synthetic fixture coverage");
                var surface=Find<PCMHammer.Views.CalibrationSurface>(window).Single();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Check(surface.ActualWidth>900&&surface.ActualHeight>grid.ActualHeight,"Graph occupies most of the editing canvas without a second operations sidebar");
                tune.SelectedArea=tune.Areas.Single(a=>a.Key=="fuel");
                Check(tune.VisibleFields.Count==1&&tune.Cells.Count==561&&tune.Surface?.Rows==33,"Fueling category opens its imported data and numeric table");
                tune.SelectedArea=tune.Areas.Single(a=>a.Key=="torque");
                Check(tune.VisibleFields.Count==0&&tune.SelectedField==null&&tune.Surface==null&&!tune.CanEdit&&tune.AreaNeeded.Contains("torque"),"Missing torque definitions show a coverage explanation without fabricated controls or stale spark data");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Capture(window,Path.Combine(folder,"coverage-preview.png"));
                tune.SelectedArea=tune.Areas.Single(a=>a.Key=="spark");
                tune.Cells[0].Text="32";tune.SelectedArea=tune.Areas.Single(a=>a.Key=="fuel");
                Check(tune.SelectedArea?.Key=="spark"&&tune.Cells[0].Text=="32","Category navigation cannot discard unsaved cell inputs");tune.DiscardCells();
                tune.TableSearch="Low-octane";Check(tune.VisibleFields.Count==1&&tune.VisibleFields.All(f=>f.Name.Contains("Low-octane",StringComparison.OrdinalIgnoreCase)),"Search filters actual table names within the selected category");tune.ClearTableSearch();
                tune.SelectedField=tune.Fields[0];
                Check(tune.BuildCoverageReport().Contains("virtual VE")&&tune.BuildCoverageReport().Contains("0x"+fixture.Address.ToString("X8")),"Coverage report lists actual mappings and unresolved model definitions");
                double splitHeight=surface.ActualHeight;tune.SelectedLayout="Graph only";
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Check(grid.Parent is System.Windows.Controls.DockPanel&&surface.ActualHeight>splitHeight*1.4,"Graph-only view expands the plotting area");
                Capture(window,Path.Combine(folder,"graph-focus-preview.png"));
                tune.SelectedLayout="Table only";
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Check(tableTabs.Visibility==Visibility.Collapsed&&grid.ActualHeight>300,"Table-only view expands the editable grid");
                tune.SelectedLayout="Graph + table";
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Capture(window,Path.Combine(folder,"ribbon-workspace-preview.png"));
                                var fieldsList=Find<System.Windows.Controls.ListBox>(window).Single(l=>l.Name=="FieldNavigation");
                Check(fieldsList.ActualHeight>90&&fieldsList.Items.Count==2,"Loaded table list remains usable below the area navigation");
                File.WriteAllText(Path.Combine(folder,"definition-coverage.md"),tune.BuildCoverageReport());
                window.Width=1180;window.Height=780;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                Check(fieldsList.ActualHeight>60&&grid.ActualWidth>650,"Category table list and editing canvas remain usable at the minimum window size");
                window.Width=1360;window.Height=900;
                if(args.Length>=5)
                {
                    byte[] posted=File.ReadAllBytes(args[3]);using var definition=File.OpenText(args[4]);
                    tune.LoadXdfPair(posted,definition,"downloaded 12619078 pair");
                    Check(tune.Fields.Count==49&&tune.TuneIdentity.Contains("12619078")&&tune.IsReference&&!tune.CanExport,"Downloaded online pair opens 49 supported items in an isolated practice workspace");
                    tune.SelectedField=tune.Fields.Single(f=>f.Name=="AirflowRPMZone_RisingThreshold");
                    Check(tune.Cells.Single().Text=="4000"&&tune.SelectedField.Storage=="f32be","Custom-marked float constant displays 4000 RPM rather than its integer bit pattern");
                    tune.SelectedField=tune.Fields.Single(f=>f.Name=="KtMAFI_dm_EngAirFlowFull");
                    Check(tune.Curve.Count==101&&tune.Surface!.Values.Max()<600&&tune.Surface.Values.Min()>0,"Posted MAF curve decodes all 101 floating-point values");
                    tune.SelectedLayout="Graph + table";tableTabs.SelectedIndex=1;
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                    Capture(window,Path.Combine(folder,"posted-pair-preview.png"));
                }
                vm.DocumentDirty=false;vm.LoadedPackage=null;
                Console.WriteLine("PASS: PCM Hammer workspace, logging, charts, profiles, edit/undo/apply guards, and mutual exclusion.");
                code=0;
            }
            catch(Exception exception){Console.Error.WriteLine(exception);}
            finally{app.Shutdown(code);}
        });
        app.Run();
        return code;
    }
    private static (byte[] Image,string Xdf,int Address) PracticeFixture(byte[] original)
    {
        // Synthetic mappings exercise import/rendering, not real calibration semantics.
        var os=FileValidator.GetE38MasterSegments(original).First(x=>x.BlockType==BlockType.OperatingSystem);
        int address=FileValidator.GetE38MasterSegments(original).First(x=>x.BlockType==BlockType.Calibration).Start+0x40;
        var definition=new CalibrationDefinition {Name="Synthetic UI fixture only",Osid=12607218,ImageSize=original.Length,OsSha256=CalibrationEditor.Hash(original,os.Start,os.Length),
            Fields={new CalibrationField {Id="fixture",Name="Synthetic bytes",Address=address,Rows=64,Columns=64,Storage="u8",Minimum=0,Maximum=255}}};
        var editor=new CalibrationEditor(original,definition,new QuietLogger());
        editor.Edit(editor.Definition.Fields[0],Enumerable.Range(0,4096).Select(i=>(double)(10+i%20)).ToArray(),"Generated test samples");
        byte[] image=editor.Export();
        var header=new XElement("XDFHEADER",new XElement("deftitle","12607218 synthetic fixture"),new XElement("BASEOFFSET",new XAttribute("offset","0"),new XAttribute("subtract","0")));
        foreach(var pair in new[]{(0,"Spark"),(1,"Fuel"),(2,"Idle"),(3,"Airflow")})header.Add(new XElement("CATEGORY",new XAttribute("index",pair.Item1),new XAttribute("name",pair.Item2)));
        var root=new XElement("XDFFORMAT",header);int next=address;
        foreach(var map in new[]{("High-octane spark � TEST MAPPING",33,33,1),("Low-octane spark � TEST MAPPING",33,33,1),("Fuel � TEST MAPPING",33,17,2),("Idle � TEST MAPPING",17,1,3),("Airflow � TEST MAPPING",33,1,4)})
        {
            XElement Axis(string id,int count)=>new XElement("XDFAXIS",new XAttribute("id",id),Enumerable.Range(0,count).Select(i=>new XElement("LABEL",new XAttribute("index",i),new XAttribute("value",i*200))));
            root.Add(new XElement("XDFTABLE",new XElement("title",map.Item1),new XElement("CATEGORYMEM",new XAttribute("index","0"),new XAttribute("category",map.Item4)),Axis("x",map.Item3),Axis("y",map.Item2),
                new XElement("XDFAXIS",new XAttribute("id","z"),new XElement("EMBEDDEDDATA",new XAttribute("mmedaddress","0x"+next.ToString("X")),new XAttribute("mmedelementsizebits","8"),new XAttribute("mmedtypeflags","0x05"),new XAttribute("mmedrowcount",map.Item2),new XAttribute("mmedcolcount",map.Item3)),new XElement("MATH",new XAttribute("equation","X"),new XElement("VAR",new XAttribute("id","X"))))));
            next+=map.Item2*map.Item3;
        }
        return(image,root.ToString(),address);
    }
    private static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Find<T>(child))yield return nested;}
    }
    private static void Capture(Window window,string path)
    {
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1360,860));root.Arrange(new Rect(0,0,1360,860));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1360,860,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var image=File.Create(path))png.Save(image);
    }
    private sealed class QuietLogger : ILogger
    {
        public void AddDebugMessage(string m){} public void AddUserMessage(string m){}
        public void StatusUpdateActivity(string m){} public void StatusUpdateTimeRemaining(string m){}
        public void StatusUpdatePercentDone(string m){} public void StatusUpdateRetryCount(string m){}
        public void StatusUpdateProgressBar(double p,bool v){} public void StatusUpdateKbps(string m){}
        public void StatusUpdateReset(){}
    }
    private sealed class SimulatedAdapter:MockDevice
    {
        public bool Initializes=true,Responds=true,SupportsCan=true,Disposed;
        public TaskCompletionSource<bool>? Pending;
        public SimulatedAdapter():base(null!,new QuietLogger()){}
        public override Task<bool> Initialize()=>Pending?.Task??Task.FromResult(Initializes);
        public override Task<bool> CheckDeviceConnection()=>Task.FromResult(Responds);
        public override IReadOnlyList<BusProtocol> SupportedProtocols=>SupportsCan?new[]{BusProtocol.Can500k}:new[]{BusProtocol.VPW};
        protected override void Dispose(bool disposing){Disposed=true;}
    }
}
