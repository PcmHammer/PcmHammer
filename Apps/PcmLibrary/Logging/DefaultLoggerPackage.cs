// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// The dashboard and monitors a fresh install opens with.
    /// </summary>
    /// <remarks>
    /// Built in code rather than shipped as a .plz file. A file beside the executable can be
    /// missing, stale or edited, and then the app has no dashboard at all and nothing to say about
    /// why; this cannot go wrong, costs no deployment step, and is reachable from tests that assert
    /// every parameter it names really exists.
    ///
    /// It is an ordinary <see cref="LoggerPackage"/> in every other way: Save As writes it out as a
    /// .plz, after which it is the user's file and this is no longer involved.
    ///
    /// Every parameter here is one the PCM itself reports, so nothing is needed but an interface and
    /// a vehicle - no auxiliary bus, no wideband, no second device. The imported AVT dashboard that
    /// this replaces had a gauge for the wideband input on an AVT box, which reads nothing for
    /// anyone without that hardware. Commanded A/F ratio takes its place: it is what the PCM is
    /// asking the injectors for, which is the number a wideband is usually being read against.
    ///
    /// The same package serves both buses. A CAN PCM is asked for the same PID numbers with service
    /// 0x22 and refuses any it does not implement, which drops the parameter with a message and
    /// leaves its gauge reading "--". That is why this is not split in two: a dashboard per bus
    /// would have to guess which PIDs a module answers, and the module itself answers that for free.
    /// </remarks>
    public static class DefaultLoggerPackage
    {
        /// <summary><see cref="PackagedSource.Kind"/> for a package that came from here.</summary>
        public const string SourceKind = "builtin";

        /// <summary>Shown where a file name would be, since there is no file.</summary>
        public const string SourceName = "PcmLogger default";

        /// <summary>
        /// Deliberately imperial, matching the .LogProfile files that already ship. Both
        /// conversions exist, so changing this is a one-line edit - but the ranges of the two
        /// temperature gauges have to change with it.
        /// </summary>
        private const string Temperature = "°F";

        public static LoggerPackage Create()
        {
            LoggerPackage package = new LoggerPackage
            {
                Description = "The parameters and gauges PcmLogger starts with.",
                Source = new PackagedSource { Kind = SourceKind, Name = SourceName },
            };

            AddPids(package);
            package.Dashboards.Add(BuildDashboard());
            package.Monitors.Add(BuildEngineMonitor());
            package.Monitors.Add(BuildFuelMonitor());

            return package;
        }

        /// <summary>
        /// What the logger polls. Fifteen PCM parameters, of which three are sixteen-bit: eighteen
        /// bytes, which is exactly three DPIDs on VPW with nothing wasted.
        /// </summary>
        private static void AddPids(LoggerPackage package)
        {
            Pid(package, "EngineSpeed", "Engine Speed", "RPM");
            Pid(package, "MAFSensor", "MAF Sensor", "g/s");
            Pid(package, "MAPSensor", "MAP Sensor", "kPa");
            Pid(package, "ThrottlePosition", "Throttle Position", "%");
            Pid(package, "IgnitionTiming", "Ignition Timing", "°");
            Pid(package, "KnockRetardDegrees", "Knock Retard Degrees", "°");
            Pid(package, "TargetAFRatio", "Target A/F Ratio", "AFR");
            Pid(package, "ECTSensor", "ECT Sensor", Temperature);
            Pid(package, "IATSensor", "IAT Sensor", Temperature);
            Pid(package, "Ignition1Signal", "Ignition 1 Signal", "Volts");
            // Use the SAE trim PIDs. The similarly named 12xx entries are GM enhanced PIDs
            // which are not as broadly available across the VPW and CAN generations.
            Pid(package, "ShortTermFTLeftBank", "Short Term FT Bank 1", "%");
            Pid(package, "LongTermFTLeftBank", "Long Term FT Bank 1", "%");
            Pid(package, "ShortTermFTRightBank", "Short Term FT Bank 2", "%");
            Pid(package, "LongTermFTRightBank", "Long Term FT Bank 2", "%");

            // The injector pulse width the duty cycle is computed from. Listed in its own right so
            // the package says plainly what it polls - the logger would pull it in regardless, as a
            // dependency of the math parameter below.
            Pid(package, "InjectorPWMLeftBankAverage", "Injector PWM Left Bank Average", "ms");

            // Both cost no bus traffic, being computed from values already in the row: duty cycle
            // from engine speed and pulse width, air per cylinder from mass airflow and engine
            // speed.
            Pid(package, "MathIdcBank1", "IDC Bank 1", "%");
            Pid(package, "MathLoadFromPids", "Load (from RPM and MAF)", "g/cyl");
        }

        private static void Pid(LoggerPackage package, string id, string name, string units)
        {
            package.Pids.Add(new PackagedPid { Id = id, Name = name, Units = units });
        }

        /// <summary>
        /// Six dials over a column and a block of readouts.
        /// </summary>
        /// <remarks>
        /// The arrangement is the one the AVT dashboard used, which is a good deal better looking
        /// than a plain grid: big dials across the top two thirds for what is watched at a glance, a
        /// narrow column of readouts down the right, and a block of them along the bottom for the
        /// numbers that are read rather than glanced at. What is on it is ours - nothing here names
        /// an interface or a file, and the wideband gauge, which was an input on an AVT box and so
        /// read nothing for anyone without one, has become commanded A/F ratio.
        /// </remarks>
        private static DashboardLayout BuildDashboard()
        {
            DashboardLayout dashboard = new DashboardLayout
            {
                Id = "dash1",
                Title = "Overview",
                Visible = true,
                Order = 0,
            };

            // Two rows of three dials, filling the left 84% down to 60%.
            Place(dashboard, left: 0.00, top: 0.00, width: 0.28, height: 0.30, gauges: new[]
            {
                Dial("EngineSpeed", "Engine Speed", "RPM", 0, 7000, 0, alarmHigh: 6200),
                Dial("ThrottlePosition", "Throttle", "%", 0, 105, 0),
                Dial("MAFSensor", "Mass Airflow", "g/s", 0, 300, 1),
            });

            Place(dashboard, left: 0.00, top: 0.30, width: 0.28, height: 0.30, gauges: new[]
            {
                Dial("MAPSensor", "Manifold Pressure", "kPa", 0, 105, 0),
                Dial("IgnitionTiming", "Spark Advance", "°", -5, 45, 1),
                Dial("TargetAFRatio", "Commanded AFR", "AFR", 7.5, 22.5, 1),
            });

            // The right-hand column, beside the dials.
            Place(dashboard, left: 0.84, top: 0.00, width: 0.16, height: 0.30, gauges: new[]
            {
                Readout("KnockRetardDegrees", "Knock Retard", "°", 0, 27, 1, alarmHigh: 0.5),
            });

            Place(dashboard, left: 0.84, top: 0.30, width: 0.16, height: 0.30, gauges: new[]
            {
                Readout("MathIdcBank1", "Injector Duty", "%", 0, 100, 1, alarmHigh: 85),
            });

            // Fuel trims together, then everything else, across the bottom.
            Place(dashboard, left: 0.00, top: 0.60, width: 0.25, height: 0.20, gauges: new[]
            {
                Readout("ShortTermFTLeftBank", "Short Term Trim 1", "%", -25, 25, 1, alarmLow: -12, alarmHigh: 12),
                Readout("LongTermFTLeftBank", "Long Term Trim 1", "%", -25, 25, 1, alarmLow: -12, alarmHigh: 12),
                Readout("ShortTermFTRightBank", "Short Term Trim 2", "%", -25, 25, 1, alarmLow: -12, alarmHigh: 12),
                Readout("LongTermFTRightBank", "Long Term Trim 2", "%", -25, 25, 1, alarmLow: -12, alarmHigh: 12),
            });

            Place(dashboard, left: 0.00, top: 0.80, width: 0.25, height: 0.20, gauges: new[]
            {
                Readout("ECTSensor", "Coolant", Temperature, 0, 260, 0, alarmHigh: 225),
                Readout("IATSensor", "Intake Air", Temperature, 0, 220, 0),
                Readout("Ignition1Signal", "Battery", "Volts", 0, 18, 1, alarmLow: 12.0, alarmHigh: 15.5),
                Readout("MathLoadFromPids", "Air per Cylinder", "g/cyl", 0, 1.5, 2),
            });

            return dashboard;
        }

        /// <summary>
        /// Lay gauges out side by side from a starting corner and add them to the dashboard.
        /// </summary>
        /// <remarks>
        /// Bounds are fractions of the dashboard area rather than pixels, so the layout keeps its
        /// proportions whatever the window is doing.
        /// </remarks>
        private static void Place(
            DashboardLayout dashboard, double left, double top, double width, double height, GaugeLayout[] gauges)
        {
            for (int index = 0; index < gauges.Length; index++)
            {
                GaugeLayout gauge = gauges[index];
                gauge.Left = left + (index * width);
                gauge.Top = top;
                gauge.Width = width;
                gauge.Height = height;
                dashboard.Gauges.Add(gauge);
            }
        }

        private static GaugeLayout Dial(
            string pid, string title, string units, double low, double high, int digits,
            double? alarmLow = null, double? alarmHigh = null)
        {
            return Build(GaugeKind.Round, pid, title, units, low, high, digits, alarmLow, alarmHigh);
        }

        private static GaugeLayout Readout(
            string pid, string title, string units, double low, double high, int digits,
            double? alarmLow = null, double? alarmHigh = null)
        {
            return Build(GaugeKind.Text, pid, title, units, low, high, digits, alarmLow, alarmHigh);
        }

        /// <remarks>
        /// The alarm pair is the band a reading is normal *inside*, so a gauge with only an upper
        /// limit takes the bottom of its scale as the lower one.
        ///
        /// NormalColor is left at zero on purpose. The view reads black as "no colour chosen" and
        /// falls back to the theme's own, which is what keeps these gauges legible in dark mode as
        /// well as light - a colour written in here would be right in one and wrong in the other.
        /// The alarm colour is stated, because red means the same thing in both.
        /// </remarks>
        private static GaugeLayout Build(
            GaugeKind kind, string pid, string title, string units, double low, double high, int digits,
            double? alarmLow, double? alarmHigh)
        {
            return new GaugeLayout
            {
                Kind = kind,
                PidId = pid,
                Title = title,
                Units = units,
                RangeLow = low,
                RangeHigh = high,
                Digits = digits,
                HasAlarms = alarmLow.HasValue || alarmHigh.HasValue,
                AlarmLow = alarmLow ?? low,
                AlarmHigh = alarmHigh ?? high,
                NormalColor = 0,
                AlarmColor = 0xFF0000,
            };
        }

        /// <summary>What an engine is doing: load, what it is being asked for, and what it minds.</summary>
        private static MonitorLayout BuildEngineMonitor()
        {
            MonitorLayout monitor = new MonitorLayout { Id = "monitor1", Title = "Engine", Order = 0 };

            Trace(monitor, "EngineSpeed", "Engine Speed", "RPM", 0, 7000);
            Trace(monitor, "MAPSensor", "MAP", "kPa", 0, 105);
            Trace(monitor, "ThrottlePosition", "Throttle", "%", 0, 100);
            Trace(monitor, "IgnitionTiming", "Spark Advance", "°", -10, 50);
            Trace(monitor, "KnockRetardDegrees", "Knock Retard", "°", 0, 25);

            return monitor;
        }

        /// <summary>What the fuelling is doing, which is the other half of reading a log.</summary>
        private static MonitorLayout BuildFuelMonitor()
        {
            MonitorLayout monitor = new MonitorLayout { Id = "monitor2", Title = "Fuelling", Order = 1 };

            Trace(monitor, "MAFSensor", "MAF", "g/s", 0, 255);
            Trace(monitor, "MathIdcBank1", "Injector Duty", "%", 0, 100);
            Trace(monitor, "ShortTermFTLeftBank", "Short Term Trim 1", "%", -25, 25);
            Trace(monitor, "LongTermFTLeftBank", "Long Term Trim 1", "%", -25, 25);
            Trace(monitor, "TargetAFRatio", "Commanded AFR", "AFR", 8, 20);

            return monitor;
        }

        /// <remarks>
        /// The colour is left at zero for the same reason the gauges' is: the monitor reads black as
        /// unset and hands out its theme palette by slot, which also keeps the traces within one
        /// monitor distinct from each other without this having to pick them.
        /// </remarks>
        private static void Trace(
            MonitorLayout monitor, string pid, string title, string units, double low, double high)
        {
            monitor.Series.Add(new MonitorSeriesLayout
            {
                PidId = pid,
                Title = title,
                Units = units,
                RangeLow = low,
                RangeHigh = high,
                Visible = true,
                Color = 0,
            });
        }
    }
}
