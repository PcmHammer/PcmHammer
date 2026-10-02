// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PcmHacking
{
    /// <summary>
    /// How a dashboard entry draws its parameter. The numbers are TunerPro's.
    /// </summary>
    public enum AdxGaugeType
    {
        Round = 0,
        Text = 1,
        Indicator = 2,
        Bar = 3,
    }

    /// <summary>
    /// One input to a parameter's equation.
    /// </summary>
    /// <remarks>
    /// A native variable is the parameter's own value, taken from the packet at its offset. A linked
    /// one is another parameter's converted value, named by that parameter's hash - so parameters
    /// form a dependency graph and have to be evaluated in order. A parameter can have no native
    /// variable at all, in which case it reads nothing from the packet and is purely derived.
    /// </remarks>
    public class AdxVariable
    {
        /// <summary>The name used in the equation. Files use both "X" and "x".</summary>
        public string VarId { get; set; } = string.Empty;

        public bool IsLink { get; set; }

        /// <summary>The parameter supplying this variable, when IsLink.</summary>
        public uint LinkIdHash { get; set; }

        public override string ToString() => this.IsLink ? $"{this.VarId} -> 0x{this.LinkIdHash:X8}" : $"{this.VarId} (native)";
    }

    /// <summary>
    /// One logged item: either a value read from the packet and run through an equation, or a bit
    /// tested against a mask. Both appear in the same lists, which is why they are one type here.
    /// </summary>
    public class AdxParameter
    {
        /// <summary>
        /// TunerPro's identity for cross-references. Dashboards, monitors and list views name their
        /// parameters by this hash rather than by id, so it is the key everything else joins on.
        /// </summary>
        public uint IdHash { get; set; }

        /// <summary>Which received packet this is read from, by that packet's hash.</summary>
        public uint ParentCommandIdHash { get; set; }

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;

        /// <summary>Byte offset into the packet body.</summary>
        public int PacketOffset { get; set; }

        /// <summary>Width in bits. Most entries omit it and inherit the file's default.</summary>
        public int SizeInBits { get; set; }

        public double RangeLow { get; set; }
        public double RangeHigh { get; set; }
        public bool HasAlarms { get; set; }
        public double AlarmLow { get; set; }
        public double AlarmHigh { get; set; }

        /// <summary>Decimal places to show.</summary>
        public int DigitCount { get; set; }

        public int OutputType { get; set; }
        public int DataType { get; set; }
        public int UnitType { get; set; }

        /// <summary>The conversion, in TunerPro's syntax, over the variables below.</summary>
        public string Equation { get; set; } = "X";

        /// <summary>The equation's inputs. Empty means the equation is over the raw value alone.</summary>
        public List<AdxVariable> Variables { get; } = new List<AdxVariable>();

        /// <summary>
        /// True when nothing in this parameter comes from the packet, so its value is computed
        /// entirely from other parameters and it must be evaluated after them.
        /// </summary>
        public bool IsDerived => this.Variables.Count > 0 && this.Variables.All(v => v.IsLink);

        /// <summary>The parameters this one needs evaluated first.</summary>
        public IEnumerable<uint> Dependencies => this.Variables.Where(v => v.IsLink).Select(v => v.LinkIdHash);

        public bool IsBitMapped { get; set; }
        public string BitOperation { get; set; } = string.Empty;
        public uint BitOperand { get; set; }
        public uint BitResult { get; set; }
        public string TrueString { get; set; } = string.Empty;
        public string FalseString { get; set; } = string.Empty;

        public override string ToString() => this.Title;
    }

    /// <summary>One gauge on a dashboard.</summary>
    /// <remarks>
    /// The bounds are percentages of the dashboard area, not pixels, so a dashboard scales to
    /// whatever it is drawn into.
    /// </remarks>
    public class AdxGauge
    {
        public AdxGaugeType GaugeType { get; set; }
        public uint ItemIdHash { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }

        /// <summary>Sweep of a round gauge, in degrees.</summary>
        public int ArcMax { get; set; }

        public uint BorderColor { get; set; }

        public int Width => this.Right - this.Left;
        public int Height => this.Bottom - this.Top;
    }

    public class AdxDashboard
    {
        public string Id { get; set; } = string.Empty;
        public uint IdHash { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<AdxGauge> Gauges { get; } = new List<AdxGauge>();

        public override string ToString() => this.Title;
    }

    /// <summary>One plotted trace on a monitor.</summary>
    public class AdxMonitorSeries
    {
        public uint ItemIdHash { get; set; }
        public uint LineColor { get; set; }
        public uint AxisColor { get; set; }
    }

    /// <summary>A strip chart: several parameters plotted against time.</summary>
    public class AdxMonitor
    {
        public string Id { get; set; } = string.Empty;
        public uint IdHash { get; set; }
        public string Title { get; set; } = string.Empty;
        public uint MainBackgroundColor { get; set; }
        public uint PlotBackgroundColor { get; set; }
        public uint PlotOutlineColor { get; set; }
        public uint TitleColor { get; set; }
        public uint TimeAxisColor { get; set; }
        public List<AdxMonitorSeries> Series { get; } = new List<AdxMonitorSeries>();

        public override string ToString() => this.Title;
    }

    /// <summary>A tabular view: a flat list of parameters shown with their current values.</summary>
    public class AdxListView
    {
        public string Id { get; set; } = string.Empty;
        public uint IdHash { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<uint> ItemIdHashes { get; } = new List<uint>();

        public override string ToString() => this.Title;
    }

    /// <summary>
    /// A packet the tool expects to receive, and where the data sits inside it.
    /// </summary>
    /// <remarks>
    /// This is the only transport-specific part of an ADX: the parameters themselves are just
    /// offsets, widths and equations, so the same definitions can be fed from a different bus.
    /// </remarks>
    public class AdxListenPacket
    {
        public string Id { get; set; } = string.Empty;
        public uint IdHash { get; set; }
        public string Title { get; set; } = string.Empty;
        public int ListenTimeout { get; set; }
        public int PacketBodyLength { get; set; }
        public int PacketOffsetInBody { get; set; }
        public int PacketSize { get; set; }

        public override string ToString() => this.Title;
    }

    /// <summary>
    /// A TunerPro ADX (data acquisition definition): what to read from the vehicle, and the
    /// dashboards, monitors and lists that display it.
    /// </summary>
    public class AdxDocument
    {
        public string Guid { get; set; } = string.Empty;
        public string UserVersion { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Baud { get; set; }

        /// <summary>Width used by any parameter that does not state its own.</summary>
        public int DefaultDataSizeInBits { get; set; } = 8;

        public int DefaultSignificantDigits { get; set; } = 2;
        public int DefaultOutputType { get; set; } = 3;
        public bool DefaultLsbFirst { get; set; }
        public bool DefaultSigned { get; set; }

        public List<AdxParameter> Parameters { get; } = new List<AdxParameter>();
        public List<AdxDashboard> Dashboards { get; } = new List<AdxDashboard>();
        public List<AdxMonitor> Monitors { get; } = new List<AdxMonitor>();
        public List<AdxListView> ListViews { get; } = new List<AdxListView>();
        public List<AdxListenPacket> ListenPackets { get; } = new List<AdxListenPacket>();

        /// <summary>The parameter a dashboard, monitor or list entry refers to, or null if absent.</summary>
        public AdxParameter? FindParameter(uint idHash)
        {
            return this.Parameters.FirstOrDefault(p => p.IdHash == idHash);
        }

        /// <summary>
        /// Parameters ordered so that anything a parameter links to is evaluated before it.
        /// </summary>
        /// <remarks>
        /// Equations can read other parameters' converted values - "Injector Duty Cycle" divides by
        /// engine RPM, and "Baro Compensated MAP" is built from two other parameters and reads
        /// nothing from the packet at all - so evaluating in file order would use stale or missing
        /// inputs. A parameter caught in a cycle is still returned, just after its cycle partners,
        /// so a malformed file loses accuracy rather than disappearing from the display.
        /// </remarks>
        public IReadOnlyList<AdxParameter> GetEvaluationOrder()
        {
            Dictionary<uint, AdxParameter> byHash = new Dictionary<uint, AdxParameter>();
            foreach (AdxParameter parameter in this.Parameters)
            {
                byHash[parameter.IdHash] = parameter;
            }

            List<AdxParameter> order = new List<AdxParameter>();
            HashSet<uint> placed = new HashSet<uint>();
            HashSet<uint> onPath = new HashSet<uint>();

            void Visit(AdxParameter parameter)
            {
                if (placed.Contains(parameter.IdHash) || !onPath.Add(parameter.IdHash))
                {
                    return;
                }

                foreach (uint dependency in parameter.Dependencies)
                {
                    if (byHash.TryGetValue(dependency, out AdxParameter? source))
                    {
                        Visit(source);
                    }
                }

                onPath.Remove(parameter.IdHash);
                if (placed.Add(parameter.IdHash))
                {
                    order.Add(parameter);
                }
            }

            foreach (AdxParameter parameter in this.Parameters)
            {
                Visit(parameter);
            }

            return order;
        }

        public static AdxDocument Load(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                return Load(stream);
            }
        }

        public static AdxDocument Load(Stream stream)
        {
            XDocument xml = XDocument.Load(stream);
            XElement? root = xml.Root;
            if (root == null || root.Name.LocalName != "ADXFORMAT")
            {
                throw new InvalidDataException("Not an ADX file: the root element is not ADXFORMAT.");
            }

            AdxDocument document = new AdxDocument();
            ReadHeader(root.Element("ADXHEADER"), document);

            foreach (XElement element in root.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "ADXVALUE":
                        document.Parameters.Add(ReadValue(element, document));
                        break;

                    case "ADXBITMASK":
                        document.Parameters.Add(ReadBitmask(element, document));
                        break;

                    case "ADXDASHBOARD":
                        document.Dashboards.Add(ReadDashboard(element));
                        break;

                    case "ADXMONITOR":
                        document.Monitors.Add(ReadMonitor(element));
                        break;

                    case "ADXLISTVIEW":
                        document.ListViews.Add(ReadListView(element));
                        break;

                    case "ADXCLISTENPACKET":
                        document.ListenPackets.Add(ReadListenPacket(element));
                        break;
                }
            }

            return document;
        }

        private static void ReadHeader(XElement? header, AdxDocument document)
        {
            if (header == null)
            {
                return;
            }

            document.Guid = (string?)header.Element("guid") ?? string.Empty;
            document.UserVersion = (string?)header.Element("userversion") ?? string.Empty;
            document.Author = (string?)header.Element("author") ?? string.Empty;
            document.Description = (string?)header.Element("desc") ?? string.Empty;
            document.Baud = ParseInt((string?)header.Element("baud"), 0);

            XElement? defaults = header.Element("DEFAULTS");
            if (defaults == null)
            {
                return;
            }

            document.DefaultDataSizeInBits = ParseInt(Attribute(defaults, "datasizeinbits"), 8);
            document.DefaultSignificantDigits = ParseInt(Attribute(defaults, "sigdigits"), 2);
            document.DefaultOutputType = ParseInt(Attribute(defaults, "outputtype"), 3);
            document.DefaultLsbFirst = ParseInt(Attribute(defaults, "lsbfirst"), 0) != 0;
            document.DefaultSigned = ParseInt(Attribute(defaults, "signed"), 0) != 0;
        }

        private static AdxParameter ReadValue(XElement element, AdxDocument document)
        {
            AdxParameter parameter = ReadCommon(element, document);

            parameter.Units = (string?)element.Element("units") ?? string.Empty;
            parameter.SizeInBits = ParseInt((string?)element.Element("sizeinbits"), document.DefaultDataSizeInBits);
            parameter.DigitCount = ParseInt((string?)element.Element("digcount"), document.DefaultSignificantDigits);
            parameter.OutputType = ParseInt((string?)element.Element("outputtype"), document.DefaultOutputType);
            parameter.DataType = ParseInt((string?)element.Element("datatype"), 0);
            parameter.UnitType = ParseInt((string?)element.Element("unittype"), 0);

            XElement? range = element.Element("range");
            if (range != null)
            {
                parameter.RangeLow = ParseDouble(Attribute(range, "low"), 0);
                parameter.RangeHigh = ParseDouble(Attribute(range, "high"), 0);
            }

            XElement? alarms = element.Element("alarms");
            if (alarms != null)
            {
                parameter.HasAlarms = true;
                parameter.AlarmLow = ParseDouble(Attribute(alarms, "low"), 0);
                parameter.AlarmHigh = ParseDouble(Attribute(alarms, "high"), 0);
            }

            XElement? math = element.Element("MATH");
            if (math != null)
            {
                string equation = Attribute(math, "equation") ?? string.Empty;
                if (equation.Length > 0)
                {
                    parameter.Equation = equation;
                }

                foreach (XElement variable in math.Elements("VAR"))
                {
                    // "link" is TunerPro's spelling; anything else is the parameter's own value.
                    string type = Attribute(variable, "type") ?? "native";
                    parameter.Variables.Add(new AdxVariable
                    {
                        VarId = Attribute(variable, "varID") ?? string.Empty,
                        IsLink = string.Equals(type, "link", StringComparison.OrdinalIgnoreCase),
                        LinkIdHash = ParseUInt(Attribute(variable, "linkIDHash"), 0),
                    });
                }
            }

            return parameter;
        }

        private static AdxParameter ReadBitmask(XElement element, AdxDocument document)
        {
            AdxParameter parameter = ReadCommon(element, document);

            parameter.IsBitMapped = true;
            parameter.SizeInBits = ParseInt((string?)element.Element("sizeinbits"), document.DefaultDataSizeInBits);
            parameter.TrueString = (string?)element.Element("truestring") ?? string.Empty;
            parameter.FalseString = (string?)element.Element("falsestring") ?? string.Empty;
            parameter.BitOperation = (string?)element.Element("bitop") ?? "AND";
            parameter.BitOperand = ParseUInt((string?)element.Element("operand"), 0);
            parameter.BitResult = ParseUInt((string?)element.Element("result"), 0);

            return parameter;
        }

        private static AdxParameter ReadCommon(XElement element, AdxDocument document)
        {
            return new AdxParameter
            {
                Id = Attribute(element, "id") ?? string.Empty,
                IdHash = ParseUInt(Attribute(element, "idhash"), 0),
                Title = Attribute(element, "title") ?? string.Empty,
                Description = (string?)element.Element("desc") ?? string.Empty,
                ParentCommandIdHash = ParseUInt((string?)element.Element("parentcmdidhash"), 0),
                PacketOffset = ParseInt((string?)element.Element("packetoffset"), 0),
                SizeInBits = document.DefaultDataSizeInBits,
            };
        }

        private static AdxDashboard ReadDashboard(XElement element)
        {
            AdxDashboard dashboard = new AdxDashboard
            {
                Id = Attribute(element, "id") ?? string.Empty,
                IdHash = ParseUInt(Attribute(element, "idhash"), 0),
                Title = Attribute(element, "title") ?? string.Empty,
            };

            foreach (XElement entry in element.Elements("ADXDGENTRY"))
            {
                dashboard.Gauges.Add(new AdxGauge
                {
                    GaugeType = (AdxGaugeType)ParseInt(Attribute(entry, "gaugetype"), 0),
                    ItemIdHash = ParseUInt(Attribute(entry, "itemidhash"), 0),
                    Left = ParseInt(Attribute(entry, "left"), 0),
                    Top = ParseInt(Attribute(entry, "top"), 0),
                    Right = ParseInt(Attribute(entry, "right"), 0),
                    Bottom = ParseInt(Attribute(entry, "bottom"), 0),
                    ArcMax = ParseInt(Attribute(entry, "arcmax"), 0),
                    BorderColor = ParseUInt(Attribute(entry, "bordercolor"), 0),
                });
            }

            return dashboard;
        }

        private static AdxMonitor ReadMonitor(XElement element)
        {
            AdxMonitor monitor = new AdxMonitor
            {
                Id = Attribute(element, "id") ?? string.Empty,
                IdHash = ParseUInt(Attribute(element, "idhash"), 0),
                Title = Attribute(element, "title") ?? string.Empty,
                MainBackgroundColor = ParseUInt((string?)element.Element("mainbkgcolor"), 0),
                PlotBackgroundColor = ParseUInt((string?)element.Element("plotbkgcolor"), 0),
                PlotOutlineColor = ParseUInt((string?)element.Element("plotoutlinecolor"), 0),
                TitleColor = ParseUInt((string?)element.Element("titlecolor"), 0),
                TimeAxisColor = ParseUInt((string?)element.Element("timeaxiscolor"), 0),
            };

            foreach (XElement entry in element.Elements("ADXMONSERIES"))
            {
                monitor.Series.Add(new AdxMonitorSeries
                {
                    ItemIdHash = ParseUInt(Attribute(entry, "itemidhash"), 0),
                    LineColor = ParseUInt(Attribute(entry, "linecolor"), 0),
                    AxisColor = ParseUInt(Attribute(entry, "axiscolor"), 0),
                });
            }

            return monitor;
        }

        private static AdxListView ReadListView(XElement element)
        {
            AdxListView view = new AdxListView
            {
                Id = Attribute(element, "id") ?? string.Empty,
                IdHash = ParseUInt(Attribute(element, "idhash"), 0),
                Title = Attribute(element, "title") ?? string.Empty,
            };

            foreach (XElement entry in element.Elements("ADXLVENTRY"))
            {
                view.ItemIdHashes.Add(ParseUInt(Attribute(entry, "itemidhash"), 0));
            }

            return view;
        }

        private static AdxListenPacket ReadListenPacket(XElement element)
        {
            return new AdxListenPacket
            {
                Id = Attribute(element, "id") ?? string.Empty,
                IdHash = ParseUInt(Attribute(element, "idhash"), 0),
                Title = Attribute(element, "title") ?? string.Empty,
                ListenTimeout = ParseInt((string?)element.Element("listentimeout"), 0),
                PacketBodyLength = ParseInt((string?)element.Element("packetbodylength"), 0),
                PacketOffsetInBody = ParseInt((string?)element.Element("packetoffsetinbody"), 0),
                PacketSize = ParseInt((string?)element.Element("packetsize"), 0),
            };
        }

        private static string? Attribute(XElement element, string name)
        {
            XAttribute? attribute = element.Attribute(name);
            return attribute?.Value;
        }

        /// <summary>
        /// ADX writes numbers both plain and as 0x-prefixed hex, sometimes for the same field in
        /// different files, so every numeric read goes through here.
        /// </summary>
        private static uint ParseUInt(string? text, uint fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            string trimmed = text!.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex)
                    ? hex
                    : fallback;
            }

            return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value)
                ? value
                : fallback;
        }

        private static int ParseInt(string? text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            string trimmed = text!.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex)
                    ? hex
                    : fallback;
            }

            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        private static double ParseDouble(string? text, double fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            return double.TryParse(text!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : fallback;
        }
    }
}
