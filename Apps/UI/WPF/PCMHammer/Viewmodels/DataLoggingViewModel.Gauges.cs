// SPDX-License-Identifier: GPL-3.0-only
// 2026-10-18: Gauges consume the recorded sample; no extra ECU polling.
using CommunityToolkit.Mvvm.ComponentModel;
using PcmHacking;
using System.Windows.Threading;

namespace PCMHammer.Viewmodels;
public partial class DataLoggingViewModel
{
    [ObservableProperty] private double gaugeRpm=double.NaN;
    [ObservableProperty] private double gaugeSpeed=double.NaN;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(GaugeSpeedMaximum))]
    private string gaugeSpeedUnits="mph";
    [ObservableProperty] private string gaugeStatus="Waiting for live readings · select Engine Speed and Vehicle Speed Sensor";
    public IReadOnlyList<string> GaugeUnitOptions {get;}=new[]{"mph","km/h"};
    public double GaugeSpeedMaximum=>GaugeSpeedUnits=="km/h"?260:160;
    private double lastSpeedKph=double.NaN;
    private DateTime lastGaugeSample=DateTime.MinValue;
    private DispatcherTimer? gaugeFreshnessTimer;
    private bool recordingIsDemo;
    partial void OnGaugeSpeedUnitsChanged(string value)=>GaugeSpeed=value=="km/h"?lastSpeedKph:lastSpeedKph/1.609344;
    partial void OnIsRunningChanged(bool value)
    {
        if(value)
        {
            GaugeRpm=GaugeSpeed=lastSpeedKph=double.NaN;lastGaugeSample=DateTime.UtcNow;
            GaugeStatus=recordingIsDemo?"DEMO · waiting for generated readings":"Waiting for vehicle readings";
            gaugeFreshnessTimer??=new DispatcherTimer(TimeSpan.FromMilliseconds(500),DispatcherPriority.Background,(_,_)=>ExpireGaugeSample(DateTime.UtcNow),dispatcher);
            gaugeFreshnessTimer.Start();
        }
        else
        {
            gaugeFreshnessTimer?.Stop();
            GaugeStatus=(recordingIsDemo?"DEMO stopped":"Recording stopped")+" · last received values";
        }
    }
    public void ApplyGaugeSample(IReadOnlyList<LogRowElement> row,bool demo)
    {
        lastGaugeSample=DateTime.UtcNow;
        var rpm=row.FirstOrDefault(v=>v.ParameterId=="EngineSpeed");
        GaugeRpm=rpm!=null&&rpm.Units.Equals("RPM",StringComparison.OrdinalIgnoreCase)&&double.IsFinite(rpm.ValueAsNumber)&&rpm.ValueAsNumber>=0?rpm.ValueAsNumber:double.NaN;
        var speed=row.FirstOrDefault(v=>v.ParameterId=="VehicleSpeedSensor");
        lastSpeedKph=double.NaN;
        if(speed!=null&&double.IsFinite(speed.ValueAsNumber)&&speed.ValueAsNumber>=0)
        {
            string units=speed.Units.ToLowerInvariant();
            if(units is "kph" or "km/h" or "kmh")lastSpeedKph=speed.ValueAsNumber;
            else if(units=="mph")lastSpeedKph=speed.ValueAsNumber*1.609344;
        }
        GaugeSpeed=GaugeSpeedUnits=="km/h"?lastSpeedKph:lastSpeedKph/1.609344;
        GaugeStatus=demo?"DEMO DATA · generated readings":"LIVE DATA · readings from the selected channels";
    }
    public void ExpireGaugeSample(DateTime now)
    {
        if(!IsRunning||(now-lastGaugeSample).TotalSeconds<=3)return;
        GaugeRpm=GaugeSpeed=lastSpeedKph=double.NaN;
        GaugeStatus=recordingIsDemo?"DEMO · waiting for fresh readings":"Waiting for fresh vehicle readings";
    }
}
