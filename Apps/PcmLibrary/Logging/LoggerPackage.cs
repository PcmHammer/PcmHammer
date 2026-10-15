// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>How a gauge draws its value.</summary>
    public enum GaugeKind
    {
        Round = 0,
        Text = 1,
        Indicator = 2,
        Bar = 3,
    }

    /// <summary>
    /// A parameter the logger should poll, and which conversion to use for it.
    /// </summary>
    /// <remarks>
    /// Parameters are referenced by id rather than copied in. The id is the join to the app's
    /// parameter database, which can be corrected and extended over time; a package that embedded
    /// every definition would freeze whatever was believed when it was written, which matters here
    /// because at least one widely circulated ADX has PID 1155 labelled as engine RPM when the
    /// authoritative P01/P59 list has it as the fuel level sensor.
    ///
    /// <see cref="Definition"/> is the escape hatch for a parameter the database does not have, so a
    /// package can still carry something unusual without the app having to ship it.
    /// </remarks>
    public class PackagedPid
    {
        /// <summary>The parameter database id, e.g. "EngineSpeed".</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Which conversion to use. A parameter often has several - TPSensor is Volts or percent -
        /// and the dash must display what the profile logs, so the choice travels with the package.
        /// </summary>
        public string Units { get; set; } = string.Empty;

        /// <summary>Display name, used when the database does not have this id.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Whether the existing logger shows this one in its enlarged readout.</summary>
        public bool Zoom { get; set; }

        /// <summary>Only for a parameter the database lacks; null when the id resolves.</summary>
        public PackagedPidDefinition? Definition { get; set; }

        public override string ToString() => this.Name.Length > 0 ? this.Name : this.Id;
    }

    /// <summary>Enough to log a parameter the app's database does not already describe.</summary>
    public class PackagedPidDefinition
    {
        /// <summary>PID number as hex, e.g. "1155".</summary>
        public string Pid { get; set; } = string.Empty;

        /// <summary>uint8, int8, uint16 or int16.</summary>
        public string StorageType { get; set; } = "uint8";

        public bool BitMapped { get; set; }

        public int BitIndex { get; set; } = -1;

        /// <summary>Conversion expression over x, in the same form the parameter database uses.</summary>
        public string Expression { get; set; } = "x";

        public string Format { get; set; } = "0.00";
    }

    /// <summary>One gauge: where it sits, what scale it draws, and which PID it reads.</summary>
    /// <remarks>
    /// Bounds are fractions of the dashboard area, not pixels, so a layout keeps its proportions at
    /// any window size.
    /// </remarks>
    public class GaugeLayout
    {
        public GaugeKind Kind { get; set; }

        /// <summary>The <see cref="PackagedPid.Id"/> this reads, or null while unbound.</summary>
        public string? PidId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Units { get; set; } = string.Empty;

        public double RangeLow { get; set; }

        public double RangeHigh { get; set; }

        public bool HasAlarms { get; set; }

        public double AlarmLow { get; set; }

        public double AlarmHigh { get; set; }

        /// <summary>Colour when a reading is outside the alarm range, as 0xRRGGBB.</summary>
        public int AlarmColor { get; set; } = 0xFF0000;

        /// <summary>Colour when a reading is normal, as 0xRRGGBB.</summary>
        public int NormalColor { get; set; } = 0x0000FF;

        public int Digits { get; set; } = 2;

        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>Sweep of a round gauge, in degrees.</summary>
        public int ArcDegrees { get; set; } = 300;

        public bool IsBound => !string.IsNullOrEmpty(this.PidId);

        public override string ToString() => this.Title;
    }

    public class DashboardLayout
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        /// <summary>Whether this dashboard is shown. One dashboard is visible today; the rest wait.</summary>
        public bool Visible { get; set; } = true;

        public int Order { get; set; }

        public List<GaugeLayout> Gauges { get; } = new List<GaugeLayout>();

        public override string ToString() => this.Title;
    }

    /// <summary>One trace on a monitor.</summary>
    public class MonitorSeriesLayout
    {
        public string? PidId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Units { get; set; } = string.Empty;

        /// <summary>Line and axis colour, as 0xRRGGBB.</summary>
        public int Color { get; set; }

        public double RangeLow { get; set; }

        public double RangeHigh { get; set; }

        /// <summary>Whether the trace is drawn; toggled from the monitor's right-click menu.</summary>
        public bool Visible { get; set; } = true;

        public bool IsBound => !string.IsNullOrEmpty(this.PidId);

        public override string ToString() => this.Title;
    }

    public class MonitorLayout
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public bool Visible { get; set; } = true;

        public int Order { get; set; }

        /// <summary>Width of the plotted time window, in seconds.</summary>
        public double TimeSpanSeconds { get; set; } = 20;

        public List<MonitorSeriesLayout> Series { get; } = new List<MonitorSeriesLayout>();

        public override string ToString() => this.Title;
    }

    /// <summary>
    /// A two-axis bin count, in the style of TunerPro's histograms - the view a VE or spark table is
    /// tuned against.
    /// </summary>
    /// <remarks>
    /// Nothing reads this yet. It is in the format from the start because adding a section later
    /// means every file written before it is a different shape; an empty list costs nothing.
    /// </remarks>
    public class HistogramLayout
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public bool Visible { get; set; }

        public int Order { get; set; }

        /// <summary>PID on the horizontal axis, and the bin edges to sort it into.</summary>
        public string? XPidId { get; set; }

        public List<double> XBins { get; } = new List<double>();

        public string? YPidId { get; set; }

        public List<double> YBins { get; } = new List<double>();

        /// <summary>The PID whose values fill the cells.</summary>
        public string? CellPidId { get; set; }

        /// <summary>Average, Min, Max or Count.</summary>
        public string Aggregation { get; set; } = "Average";

        /// <summary>Cells with fewer samples than this are not trustworthy and are shown as empty.</summary>
        public int MinimumSamples { get; set; } = 1;

        public override string ToString() => this.Title;
    }

    /// <summary>
    /// How the log was, or should be, collected. Mirrors what a .phz records about a vehicle.
    /// </summary>
    public class PackagedCommunications
    {
        /// <summary>VPW, CAN500k, and so on.</summary>
        public string Protocol { get; set; } = string.Empty;

        /// <summary>Serial or J2534.</summary>
        public string DeviceCategory { get; set; } = string.Empty;

        /// <summary>The J2534 device name, or the serial port.</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>The operating system id this was built against, 0 when unknown.</summary>
        public uint Osid { get; set; }

        public bool FourXReadWrite { get; set; }
    }

    /// <summary>Where a package came from, for traceability.</summary>
    public class PackagedSource
    {
        /// <summary>"adx", "manual", and so on.</summary>
        public string Kind { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Guid { get; set; } = string.Empty;
    }

    /// <summary>
    /// The contents of a .plz: the PIDs to log and every way of displaying them.
    /// </summary>
    /// <remarks>
    /// This is PcmHammer's own format, not a view over an ADX. An import reads a TunerPro file once
    /// and produces one of these; afterwards nothing depends on ADX semantics, so a package can
    /// reference CAN parameters that no ADX could have described.
    ///
    /// Unlike a .phz there are no checksums. A .phz protects flash images, where a corrupt byte can
    /// brick a PCM; the worst a damaged dashboard can do is draw badly, and a checksum that refused
    /// to open it would be more obstructive than useful.
    /// </remarks>
    public class LoggerPackage
    {
        /// <summary>Bumped when the shape changes in a way older readers cannot cope with.</summary>
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;

        public string Description { get; set; } = string.Empty;

        public DateTime Created { get; set; } = DateTime.UtcNow;

        public string CreatedBy { get; set; } = string.Empty;

        public PackagedSource Source { get; set; } = new PackagedSource();

        public PackagedCommunications Communications { get; set; } = new PackagedCommunications();

        /// <summary>What to log. Everything displayed should reference one of these.</summary>
        public List<PackagedPid> Pids { get; } = new List<PackagedPid>();

        public List<DashboardLayout> Dashboards { get; } = new List<DashboardLayout>();

        public List<MonitorLayout> Monitors { get; } = new List<MonitorLayout>();

        public List<HistogramLayout> Histograms { get; } = new List<HistogramLayout>();

        public int UnboundGaugeCount => this.Dashboards.Sum(d => d.Gauges.Count(g => !g.IsBound));

        public PackagedPid? FindPid(string? id)
        {
            return id == null
                ? null
                : this.Pids.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Display references that name a PID the package does not carry. A reader should surface
        /// these rather than drawing a gauge that can never show a value.
        /// </summary>
        public IEnumerable<string> DanglingPidReferences()
        {
            IEnumerable<string?> referenced = this.Dashboards.SelectMany(d => d.Gauges).Select(g => g.PidId)
                .Concat(this.Monitors.SelectMany(m => m.Series).Select(s => s.PidId))
                .Concat(this.Histograms.SelectMany(h => new[] { h.XPidId, h.YPidId, h.CellPidId }));

            return referenced
                .Where(id => !string.IsNullOrEmpty(id) && this.FindPid(id) == null)
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Take the presentation half of an ADX: dashboards, monitors, and the display properties of
        /// the parameters they refer to. Acquisition - packet offsets, commands, framing - is not
        /// read, which is what lets any ADX be imported whatever interface it was written for.
        /// Gauges come back unbound; binding them to PIDs is a separate decision.
        /// </summary>
        public static LoggerPackage FromAdx(AdxDocument document, string sourceName)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            LoggerPackage package = new LoggerPackage
            {
                Description = "Imported from " + sourceName,
                Source = new PackagedSource { Kind = "adx", Name = sourceName ?? string.Empty, Guid = document.Guid },
            };

            int order = 0;
            foreach (AdxDashboard dashboard in document.Dashboards)
            {
                DashboardLayout target = new DashboardLayout
                {
                    Id = "dash" + (order + 1),
                    Title = dashboard.Title,
                    // Only the first is shown; the rest are carried but hidden.
                    Visible = order == 0,
                    Order = order,
                };

                foreach (AdxGauge gauge in dashboard.Gauges)
                {
                    // Entries whose item hash is zero, or which name a parameter the file never
                    // defines, have nothing to draw; real files contain both.
                    AdxParameter? parameter = document.FindParameter(gauge.ItemIdHash);
                    if (parameter == null)
                    {
                        continue;
                    }

                    target.Gauges.Add(new GaugeLayout
                    {
                        Kind = (GaugeKind)(int)gauge.GaugeType,
                        Title = parameter.Title,
                        Units = parameter.Units,
                        RangeLow = parameter.RangeLow,
                        RangeHigh = parameter.RangeHigh,
                        HasAlarms = parameter.HasAlarms,
                        AlarmLow = parameter.AlarmLow,
                        AlarmHigh = parameter.AlarmHigh,
                        Digits = parameter.DigitCount,
                        Left = gauge.Left / 100.0,
                        Top = gauge.Top / 100.0,
                        Width = gauge.Width / 100.0,
                        Height = gauge.Height / 100.0,
                        ArcDegrees = gauge.ArcMax > 0 ? gauge.ArcMax : 300,
                    });
                }

                package.Dashboards.Add(target);
                order++;
            }

            order = 0;
            foreach (AdxMonitor monitor in document.Monitors)
            {
                MonitorLayout target = new MonitorLayout
                {
                    Id = "monitor" + (order + 1),
                    Title = monitor.Title,
                    Visible = true,
                    Order = order,
                };

                foreach (AdxMonitorSeries series in monitor.Series)
                {
                    AdxParameter? parameter = document.FindParameter(series.ItemIdHash);
                    if (parameter == null)
                    {
                        continue;
                    }

                    target.Series.Add(new MonitorSeriesLayout
                    {
                        Title = parameter.Title,
                        Units = parameter.Units,
                        Color = ToRgb(series.LineColor),
                        RangeLow = parameter.RangeLow,
                        RangeHigh = parameter.RangeHigh,
                    });
                }

                package.Monitors.Add(target);
                order++;
            }

            return package;
        }

        /// <summary>
        /// ADX stores colours as 0x00BBGGRR, the Win32 COLORREF order, so the outer channels swap.
        /// </summary>
        private static int ToRgb(uint adxColor)
        {
            int r = (int)(adxColor & 0xFF);
            int g = (int)((adxColor >> 8) & 0xFF);
            int b = (int)((adxColor >> 16) & 0xFF);
            return (r << 16) | (g << 8) | b;
        }
    }
}
