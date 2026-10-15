// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking
{
    /// <summary>
    /// Stores the math expression that converts a value from the PCM into 
    /// something humans can understand.
    /// </summary>
    public class Conversion
    {
        public string Units { get; private set; }

        public string Expression { get; private set; }

        public string Format { get; private set; }

        public bool IsBitMapped { get; private set; }

        public int BitIndex { get; private set; }

        public string? TrueValue { get; private set; }

        public string? FalseValue { get; private set; }

        /// <summary>
        /// Low end of the expected reading in these units, for a gauge or chart that needs a scale
        /// before any data has arrived.
        /// </summary>
        public double RangeLow { get; private set; }

        /// <summary>High end of the expected reading in these units.</summary>
        public double RangeHigh { get; private set; }

        /// <summary>
        /// Whether a usable scale is known. False means a display should work one out from the data,
        /// rather than drawing everything flat against a range of zero.
        /// </summary>
        public bool HasRange => this.RangeHigh > this.RangeLow;

        public Conversion(string units, string expression, string format)
            : this(units, expression, format, 0, 0)
        {
        }

        public Conversion(string units, string expression, string format, double rangeLow, double rangeHigh)
        {
            this.Units = units;
            this.Expression = Sanitize(expression);
            this.Format = format;
            this.IsBitMapped = false;
            this.BitIndex = -1;
            this.TrueValue = null;
            this.FalseValue = null;
            this.RangeLow = rangeLow;
            this.RangeHigh = rangeHigh;
        }

        public Conversion(string units, int bitIndex, string trueValue, string falseValue)
        {
            this.Units = units;
            this.Expression = "x";
            this.Format = "";
            this.IsBitMapped = true;
            this.BitIndex = bitIndex;
            this.TrueValue = trueValue;
            this.FalseValue = falseValue;
        }

        public override string ToString()
        {
            return this.Units;
        }

        public static Conversion DefaultConversion = new Conversion("raw", "x", "0");

        /// <summary>
        /// The expression parser doesn't support bit-shift operators.
        /// So we hack them into division operators here.
        /// It's not pretty, but it's less ugly than changing the
        /// expressions in the XML file.
        /// </summary>
        private string Sanitize(string input)
        {
            int startIndex = input.IndexOf(">>");
            if (startIndex == -1)
            {
                return input;
            }

            int endIndex = startIndex;
            char shiftChar = ' ';
            for (int index = startIndex + 2; index < input.Length; index++)
            {
                endIndex = index;
                shiftChar = input[index];
                if (shiftChar == ' ')
                {
                    continue;
                }
                else
                {
                    endIndex++;
                    break;
                }
            }

            int shiftCount = shiftChar - '0';
            if (shiftCount < 0 || shiftCount > 15)
            {
                throw new InvalidOperationException(
                    string.Format("Unable to parse >> operator in \"{0}\"", input));
            }

            string oldText = input.Substring(startIndex, endIndex - startIndex);
            string newText = string.Format("/{0}", Math.Pow(2, shiftCount));
            return input.Replace(oldText, newText);
        }
    }

    /// <summary>
    /// Base class for various parameter types (PID, RAM, Math)
    /// </summary>
    public abstract class Parameter : IEqualityComparer<Parameter>
    {
        private static readonly IEnumerable<Conversion> noConversions = new List<Conversion>();

        public string Id { get; protected set; }
        public string Name { get; protected set; }
        public string Description { get; protected set; }
        public IEnumerable<Conversion> Conversions { get; protected set; }

        /// <summary>
        /// What family this parameter belongs to - "SAE", "GM Enhanced", "RAM", "Math", or the name
        /// of a device broadcasting on an auxiliary bus.
        /// </summary>
        /// <remarks>
        /// For grouping the parameter list, which is the only way a list of this size stays
        /// navigable. It says nothing about which bus carries the parameter: a GM enhanced PID is
        /// requested the same way over VPW and over CAN, so the transport is a property of the
        /// vehicle, not of the parameter.
        /// </remarks>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Which family obtains this parameter, matching an <see cref="IParameterSource.Name"/>.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Category"/>, which is how the list is grouped for a human and is
        /// not always the same thing: SAE and GM Enhanced are two categories a user navigates by, but
        /// one source, because both are requested the same way.
        ///
        /// Data rather than a C# type, so that a new kind of parameter is a definition file plus one
        /// class, with nothing above it needing to learn the new type.
        /// </remarks>
        public string Source { get; set; } = ParameterSources.GmEnhancedObd;

        /// <summary>
        /// The bus this parameter only ever appears on, or null when either will do.
        /// </summary>
        /// <remarks>
        /// A property of the parameter, not of its source. Broadcast covers both a vendor gauge and a
        /// cluster message, and only the first is tied to a bus: AEM and Plex build CAN products and
        /// frame their data themselves, so those parameters exist nowhere else, while a VPW broadcast
        /// read the same way is not constrained at all.
        /// </remarks>
        public BusProtocol? RequiredBus { get; set; }

        public Parameter()
        {
            this.Id = "";
            this.Name = "";
            this.Description = "";
            this.Conversions = noConversions;
        }

        public override string ToString()
        {
            return this.Name;
        }

        public Conversion GetConversion(string units)
        {
            return this.Conversions.FirstOrDefault(c => c.Units == units);
        }

        public abstract bool IsSupported(uint osid);

        public bool Equals(Parameter x, Parameter y)
        {
            return x.Id == y.Id;
        }

        public int GetHashCode(Parameter obj)
        {
            return obj.Id.GetHashCode();
        }
    }

    /// <summary>
    /// Base class for parameters that come directly from the PCM - as opposed 
    /// to Math parameters, which are only indirectly from the PCM.
    /// </summary>
    public abstract class PcmParameter : Parameter
    {
        public string StorageType { get; private set; }

        public PcmParameter(string storageType)
        {
            this.StorageType = storageType;
        }

        public int ByteCount
        {
            get
            {
                switch(this.StorageType)
                {
                    case "uint8":
                    case "int8":
                        return 1;

                    case "uint16":
                    case "int16":
                        return 2;

                    default:
                        return 0;
                }
            }
        }

        public bool IsSigned
        {
            get
            {
                switch (this.StorageType)
                {
                    case "int8":
                    case "int16":
                        return true;

                    case "uint8":
                    case "uint16":
                        return false;

                    default:
                        return false;
                }
            }
        }

        public bool BitMapped { get; protected set; }
    }

    /// <summary>
    /// These parameters have a PID number that is the same for all operating
    /// systems. (Though not not all operating systems support all PIDs.)
    /// </summary>
    public class PidParameter : PcmParameter
    {
        public IEnumerable<uint> Osids { get; private set; }
        public uint PID { get; private set; }

        /// <summary>
        /// Constructor for standard PID parameters.
        /// </summary>
        public PidParameter(
            string id,
            string name,
            string description,
            string storageType,
            bool bitMapped,
            IEnumerable<Conversion> conversions,
            uint pid,
            IEnumerable<uint> osids) : base(storageType)
        {
            this.Id = id;
            this.PID = pid;
            this.Name = name;
            this.Description = description;
            this.BitMapped = bitMapped;
            this.Conversions = conversions;
            this.Osids = osids;
        }

        public override bool IsSupported(uint osid)
        {
            return !Osids.Any() || Osids.Contains(osid); //blank list of osids means all are supported
        }
    }

    /// <summary>
    /// These parameters are read from RAM in the PCM, and the RAM addresses
    /// are unique to each operating system.
    /// </summary>
    public class RamParameter : PcmParameter
    {
        private readonly Dictionary<uint, uint> addresses;

        /// <summary>
        /// Constructor for RAM parameters.
        /// </summary>
        public RamParameter(
            string id,
            string name,
            string description,
            string storageType,
            bool bitMapped,
            IEnumerable<Conversion> conversions,
            Dictionary<uint, uint> addresses) : base(storageType)
        {
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.BitMapped = bitMapped;
            this.Conversions = conversions;
            this.addresses = addresses;
        }

        public bool TryGetAddress(uint osid, out uint address)
        {
            return this.addresses.TryGetValue(osid, out address);
        }

        public override bool IsSupported(uint osid)
        {
            uint address;
            return this.TryGetAddress(osid, out address);
        }
    }

    /// <summary>
    /// These parameters are computed from other parameters.
    /// </summary>
    public class MathParameter : Parameter
    {
        public LogColumn XColumn { get; private set; }
        public LogColumn YColumn { get; private set; }

        public MathParameter(
            string id,
            string name,
            string description,
            IEnumerable<Conversion> conversions,
            LogColumn xColumn,
            LogColumn yColumn)
        {
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.Conversions = conversions;

            this.XColumn = xColumn;
            this.YColumn = yColumn;
        }

        public override bool IsSupported(uint osid)
        {
            return this.XColumn.Parameter.IsSupported(osid) && this.YColumn.Parameter.IsSupported(osid);
        }
    }

    public enum Aggregation
    {
        Last,
        Sum,
        Average,
        Max
    }

    /// <summary>
    /// A parameter carried in a broadcast message rather than answered on request: a message
    /// identifier, the bytes within it, and how to convert them.
    /// </summary>
    /// <remarks>
    /// Not CAN-specific, despite where it came from. The same shape describes a VPW broadcast, whose
    /// three header bytes serve as the identifier - which is why the secondary bus can be either.
    /// </remarks>
    public class BusParameter : Parameter
    {
        public uint MessageId { get; private set; }
        public uint ByteIndex { get; private set; }
        public uint ByteCount { get; private set; }
        public bool HighByteFirst { get; private set; }
        public Conversion? SelectedConversion { get; set; }
        public Aggregation Aggregation { get; private set; }

        /// <summary>
        /// Always true: a broadcast parameter belongs to whichever module transmits it, so the PCM's
        /// operating system has no say in whether it is available.
        /// </summary>
        public override bool IsSupported(uint osid) { return true; }

        public BusParameter(
            uint messageId,
            uint byteIndex,
            uint byteCount,
            bool highByteFirst,
            string id,
            string name,
            string description,
            IEnumerable<Conversion> conversions,
            Aggregation aggregation)
        {
            this.MessageId = messageId;
            this.ByteIndex = byteIndex;
            this.ByteCount = byteCount;
            this.HighByteFirst = highByteFirst;
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.Conversions = conversions;
            this.Aggregation = aggregation;
            this.Aggregation = aggregation;
        }
    }
}
