// SPDX-License-Identifier: GPL-3.0-only
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PcmHacking;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using PCMHammer.Views;

namespace PCMHammer.Viewmodels;

public partial class DataChannel : ObservableObject
{
    public Parameter Parameter { get; }
    public string Name => Parameter.Name;
    public IEnumerable<Conversion> Conversions => Parameter.Conversions;
    [ObservableProperty] private bool selected;
    [ObservableProperty] private Conversion conversion;
    [ObservableProperty] private string value = "—";
    public DataChannel(Parameter parameter, bool selected)
    {
        Parameter = parameter;
        this.selected = selected;
        conversion = parameter.Conversions.FirstOrDefault(c => c.Units == "°F")
            ?? parameter.Conversions.FirstOrDefault() ?? PcmHacking.Conversion.DefaultConversion;
    }
    public LogColumn Column() => new(Parameter, Conversion, false);
}

public partial class DataLoggingViewModel : ObservableObject
{
    private readonly ILogger logger;
    private readonly Action<bool> setBusy;
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private readonly System.Windows.Threading.Dispatcher dispatcher;
    public ObservableCollection<DataChannel> Channels { get; } = new();
    public IReadOnlyList<string> Profiles {get;} = new[]{"Custom"}.Concat(TuningLogProfiles.All.Keys).ToArray();
    [ObservableProperty] private string selectedProfile="Custom";
    [ObservableProperty] private IReadOnlyList<string> plotOptions=Array.Empty<string>();
    [ObservableProperty] private string selectedPlot=string.Empty;
    [ObservableProperty] private IReadOnlyList<PlotPoint> chartPoints=Array.Empty<PlotPoint>();
    [ObservableProperty] private string chartStatus="Live window: most recent 600 complete samples · hover to inspect · wheel to zoom · double-click to reset";
    private bool applyingProfile;
    private RecordingData? loadedRecording;
    private readonly List<(double Time,IReadOnlyList<LogRowElement> Values)> recent=new();
    private DateTime recordingStarted;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private Vehicle? vehicle;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private bool isHostBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEditChannels))]
    [NotifyPropertyChangedFor(nameof(StartStopText))] [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private bool isRunning;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    private bool demoMode;
    [ObservableProperty] private string message = "Select readings, then start recording to a CSV file. A compatible adapter is required for vehicle data.";
    [ObservableProperty] private string recordingPath = "No recording yet";
    [ObservableProperty] private long sampleCount;
    [ObservableProperty] private string lastSampleTime = "No samples received";
    public bool CanEditChannels => !IsRunning;
    public string StartStopText => IsRunning ? "Stop Recording" : "Start Recording…";
    public bool CanStartOrStop => IsRunning || (!IsHostBusy && (DemoMode || Vehicle != null) && Channels.Any(c => c.Selected));

    public DataLoggingViewModel(ILogger logger, Action<bool> setBusy)
    {
        this.logger = logger;
        this.setBusy = setBusy;
        dispatcher = Application.Current.Dispatcher;
        try
        {
            var database = new ParameterDatabase(AppContext.BaseDirectory);
            database.LoadDatabase();
            string[] ids = new[] { "EngineSpeed", "VehicleSpeedSensor", "ECTSensor", "IATSensor", "MAFSensor", "MAPSensor",
                "ThrottlePosition", "IgnitionTiming", "KnockRetardDegrees", "Ignition1Signal", "EngineLoad",
                "ShortTermFTLeftBank", "LongTermFTLeftBank", "ShortTermFTRightBank", "LongTermFTRightBank",
                "TargetAFRatio", "InjectorPWMLeftBankAverage" }.Concat(TuningLogProfiles.All.Values.SelectMany(v=>v)).Distinct().ToArray();
            var defaults = new HashSet<string> { "EngineSpeed", "VehicleSpeedSensor", "ECTSensor", "MAFSensor", "MAPSensor",
                "ThrottlePosition", "IgnitionTiming", "ShortTermFTLeftBank", "LongTermFTLeftBank" };
            foreach (string id in ids)
                if (database.TryGetParameter<PidParameter>(id, out var parameter))
                {
                    var channel = new DataChannel(parameter, defaults.Contains(id));
                    channel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DataChannel.Selected)) {StartStopCommand.NotifyCanExecuteChanged();if(!applyingProfile)SelectedProfile="Custom";} };
                    Channels.Add(channel);
                }
            PlotOptions=Channels.Select(c=>Heading(c.Parameter.Name,c.Conversion.Units)).ToArray();
            SelectedPlot=PlotOptions.FirstOrDefault()??string.Empty;
        }
        catch (Exception exception)
        {
            Message = "Logging definitions could not be loaded: " + exception.Message;
            logger.AddUserMessage(Message);
        }
    }

    partial void OnSelectedProfileChanged(string value)
    {
        if(IsRunning||value=="Custom"||!TuningLogProfiles.All.TryGetValue(value,out var ids))return;
        applyingProfile=true;try{foreach(var channel in Channels)channel.Selected=ids.Contains(channel.Parameter.Id);}finally{applyingProfile=false;}
        Message=value+": candidate channels selected. The ECU checks availability when recording starts.";
    }
    partial void OnSelectedPlotChanged(string value)=>UpdateChart();
    private static string Heading(string name,string units)=>name+" ("+units+")";
    private void UpdateChart()
    {
        if(loadedRecording!=null)
        {
            int index=Array.IndexOf(loadedRecording.Headings,SelectedPlot);
            ChartPoints=index<0?Array.Empty<PlotPoint>():loadedRecording.Samples.Select((r,i)=>new PlotPoint(loadedRecording.Seconds[i],r[index])).ToArray();
        }
        else ChartPoints=recent.Select(frame=>new {frame.Time,Value=frame.Values.FirstOrDefault(v=>Heading(v.ParameterName,v.Units)==SelectedPlot)})
            .Where(v=>v.Value!=null).Select(v=>new PlotPoint(v.Time,v.Value!.ValueAsNumber)).ToArray();
    }
    [RelayCommand]
    public void OpenRecording()
    {
        if(IsRunning)return;
        var picker=new OpenFileDialog {Title="Open PCM Hammer recording",Filter="CSV recording (*.csv)|*.csv"};
        if(picker.ShowDialog(Application.Current.MainWindow)!=true)return;
        try{using var input=File.OpenText(picker.FileName);LoadRecording(RecordingCsvReader.Read(input));RecordingPath=picker.FileName;}
        catch(Exception error){Message="Could not open recording: "+error.Message;}
    }
    public void LoadRecording(RecordingData recording)
    {
        if(IsRunning)throw new InvalidOperationException("Stop recording before opening another log.");
        GaugeRpm=GaugeSpeed=lastSpeedKph=double.NaN;GaugeStatus="Recording review · gauges are reserved for live readings";
        loadedRecording=recording;PlotOptions=recording.Headings;SelectedPlot=PlotOptions.FirstOrDefault()??string.Empty;
        ChartStatus=$"Opened {recording.Samples.Count:N0} samples · {(recording.HasDemoData?"CONTAINS DEMO DATA":"vehicle recording")} · x axis: elapsed seconds";
        UpdateChart();Message="Recording opened for analysis. Select a channel above the chart.";
    }

    [RelayCommand(CanExecute = nameof(CanStartOrStop), AllowConcurrentExecutions = true)]
    public async Task StartStop()
    {
        if (IsRunning) { Message = "Stopping after the current adapter request…"; cancellation?.Cancel(); return; }
        if (!CanStartOrStop) return;
        LogColumn[] columns = Channels.Where(c => c.Selected).Select(c => c.Column()).ToArray();
        if (columns.Length > 12) { Message = "Select at most 12 readings. Fewer readings refresh faster."; return; }
        bool demo = DemoMode;
        var dialog = new SaveFileDialog {
            Title = demo ? "Save DEMO recording" : "Save vehicle recording", Filter = "CSV data log (*.csv)|*.csv",
            FileName = (demo ? "DEMO-" : "PCM-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv",
            DefaultExt = ".csv", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        await RecordTo(dialog.FileName, columns, demo);
    }

    // Kept separate from the file picker so the actual recording lifecycle can be integration-tested.
    public async Task RecordTo(string path, IReadOnlyList<LogColumn> columns, bool demo)
    {
        if (IsRunning || IsHostBusy || (!demo && Vehicle == null)) return;
        Vehicle? activeVehicle = Vehicle;
        var stop = new CancellationTokenSource();
        cancellation = stop;
        recordingIsDemo=demo;
        IsRunning = true;
        setBusy(true);
        RecordingPath = path;
        loadedRecording=null;recent.Clear();ChartPoints=Array.Empty<PlotPoint>();recordingStarted=DateTime.UtcNow;
        PlotOptions=columns.Select(c=>Heading(c.Parameter.Name,c.Conversion.Units)).ToArray();SelectedPlot=PlotOptions.FirstOrDefault()??string.Empty;
        ChartStatus="Live window: last 600 complete samples · x axis: elapsed seconds · hover / wheel to inspect";
        SampleCount = 0;
        LastSampleTime = "No samples received";
        foreach (DataChannel channel in Channels) channel.Value = "—";
        Message = demo ? "DEMO DATA — no vehicle connected." : "Identifying PCM and checking selected readings…";
        try
        {
            worker = Task.Run(async () =>
            {
                IParameterSource source;
                if (demo) source = new DemoDataSource();
                else
                {
                    DetectedModule? pcm = await activeVehicle!.DetectAndSelectPcm(stop.Token);
                    stop.Token.ThrowIfCancellationRequested();
                    if (pcm == null) throw new InvalidOperationException("No PCM answered. Check the adapter and ignition.");
                    if (pcm.Bus != BusProtocol.Can500k)
                        throw new InvalidOperationException("This integrated preview currently supports CAN logging, including E38. Use standalone PCM Logger for VPW.");
                    source = new PcmParameterSource(activeVehicle, pcm.Osid, logger);
                }
                using (source)
                using (var file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    await LiveDataRecording.Run(source, columns, file, demo ? "DEMO" : "Vehicle", row =>
                    {
                        dispatcher.Invoke(() =>
                        {
                            var values = row.ToDictionary(r => r.ParameterId, StringComparer.OrdinalIgnoreCase);
                            foreach (DataChannel channel in Channels.Where(c => c.Selected))
                                channel.Value = values.TryGetValue(channel.Parameter.Id, out var reading) ? reading.ValueAsString : "Unavailable";
                            ApplyGaugeSample(row,demo);
                            SampleCount++;
                            recent.Add(((DateTime.UtcNow-recordingStarted).TotalSeconds,row));
                            if(recent.Count>600)recent.RemoveAt(0);
                            UpdateChart();
                            LastSampleTime = "Last sample: " + DateTime.Now.ToString("HH:mm:ss");
                            Message = demo ? "Recording DEMO DATA — no vehicle data is being collected." : "Recording live PCM data. Unsupported readings are marked Unavailable.";
                        });
                    }, stop.Token);
                }
            });
            await worker;
        }
        catch (OperationCanceledException) { Message = $"Stopped. {SampleCount} samples saved."; }
        catch (Exception exception)
        {
            Message = "Recording stopped: " + exception.Message;
            logger.AddUserMessage(Message);
            logger.AddDebugMessage(exception.ToString());
        }
        finally
        {
            worker = null;
            cancellation = null;
            stop.Dispose();
            IsRunning = false;
            setBusy(false);
            if (SampleCount == 0) LastSampleTime = "No samples recorded";
        }
    }

    public async Task StopAsync()
    {
        cancellation?.Cancel();
        if (worker != null) { try { await worker; } catch { /* RecordTo reports the error. */ } }
    }

    private sealed class DemoDataSource : IParameterSource
    {
        private IReadOnlyList<LogColumn> columns = Array.Empty<LogColumn>();
        private int sample;
        public string Name => "Demo";
        public IReadOnlyList<BusProtocol> SupportedBuses => Array.Empty<BusProtocol>();
        public bool CanProvide(Parameter p) => true;
        public Task<IReadOnlyList<ParameterAvailability>> Probe(IReadOnlyList<Parameter> p, CancellationToken c) => Task.FromResult<IReadOnlyList<ParameterAvailability>>(Array.Empty<ParameterAvailability>());
        public Task<bool> Open(IReadOnlyList<LogColumn> p, CancellationToken c) { columns=p; return Task.FromResult(p.Count>0); }
        public Task Start(CancellationToken c) => Task.CompletedTask;
        public Task Suspend() => Task.CompletedTask;
        public Task Resume() => Task.CompletedTask;
        public IEnumerable<string> GetColumnNames() => columns.Select(c => LogColumnHeading.For(c.Parameter,c.Conversion.Units));
        public Task Close() => Task.CompletedTask;
        public void Dispose() { }
        public Task<IReadOnlyList<LogRowElement>> Read(CancellationToken c)
        {
            sample++;
            IReadOnlyList<LogRowElement> row = columns.Select((p,i) => {
                double value = p.Parameter.Id == "EngineSpeed" ? 1800 + 1000*Math.Sin(sample/12.0)
                    : p.Parameter.Id == "VehicleSpeedSensor" ? (40+20*Math.Sin(sample/18.0)) / (p.Conversion.Units=="mph"?1.609344:1)
                    : p.Conversion.Units == "°F" ? 185 + 2*Math.Sin(sample/20.0)
                    : p.Conversion.Units == "°C" ? 85 + Math.Sin(sample/20.0)
                    : 5 + i + Math.Sin(sample/10.0);
                return new LogRowElement(p.Parameter.Id,p.Parameter.Name,p.Conversion.Units,value.ToString("F2"),value);
            }).ToArray();
            return Task.FromResult(row);
        }
    }
}
