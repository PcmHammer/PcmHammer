// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace PcmHacking
{
    /// <summary>
    /// This loads the collection of known parameters from XML files.
    /// </summary>
    public class ParameterDatabase
    {
        private string pathToXmlDirectory = null!;

        private List<Parameter> parameters = new List<Parameter>();
        private Dictionary<UInt32, IEnumerable<BusParameter>> busParameters = new Dictionary<UInt32, IEnumerable<BusParameter>>();

        public IEnumerable<BusParameter> BusParameters
        {
            get
            {
                foreach(IEnumerable<BusParameter> parameterSet in busParameters.Values)
                {
                    foreach(BusParameter parameter in parameterSet)
                    {
                        yield return parameter;
                    }
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public ParameterDatabase(string pathToXmlDirectory)
        {
            this.pathToXmlDirectory = pathToXmlDirectory;
        }

        /// <summary>
        /// For test use only.
        /// </summary>
        public ParameterDatabase(Dictionary<UInt32, IEnumerable<BusParameter>> busParameters)
        {
            this.busParameters = busParameters;
        }

        /// <summary>
        /// Gets a parameter using the specified generic type, with the specified id.
        /// </summary>
        /// <typeparam name="T">The type of parameter, must be subclass of Parameter</typeparam>
        /// <param name="id">the string ID of the parameter to look for</param>
        /// <returns>The parameter, if found.</returns>
        public bool TryGetParameter<T>(string id, out T result) where T : Parameter
        {
            try
            {
                result = (this.parameters.First(p => p is T && p.Id == id) as T)!;
                return true;
            }
            catch(InvalidOperationException)
            {
                uint pid;
                if (typeof(T) == typeof(PidParameter) &&
                    uint.TryParse(id, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pid))
                {
                    result = (this.parameters.FirstOrDefault(p => (p as PidParameter)?.PID == pid) as T)!;
                    if (result != null)
                    {
                        return true;
                    }                    
                }

                result = null!;
                return false;
            }
        }

        public IReadOnlyDictionary<UInt32, IEnumerable<BusParameter>> GetBusParameters()
        {
            return this.busParameters;
        }

        /// <summary>
        /// returns a list of parameters that support the specified os
        /// </summary>
        /// <param name="osId">the os to search for</param>
        /// <returns>an ienumerable of parameters</returns>
        public IEnumerable<Parameter> ListParametersBySupportedOs(uint osId)
        {
            return this.parameters.Where(p => p.IsSupported(osId));
        }

        /// <summary>
        /// The parameters one connection could supply: supported by this operating system, and from
        /// a source that connection carries.
        /// </summary>
        /// <remarks>
        /// Not filtered by bus. The protocol is not known until the interface connects, so a list
        /// shows everything its connection could supply either way and anything that turns out not
        /// to work is dropped by name when logging starts.
        ///
        /// Spans both collections, so that which list a parameter appears in is decided by its
        /// source rather than by which file it was loaded from.
        /// </remarks>
        public IEnumerable<Parameter> ListParametersForConnection(uint osId, IReadOnlyList<string> sources)
        {
            return this.parameters
                .Concat(this.BusParameters)
                .Where(p => p.IsSupported(osId) && sources.Contains(p.Source));
        }

        /// <summary>
        /// adds a parameter to the database, will not allow parameters with duplicate IDs, throws exception in that case.
        /// </summary>
        /// <param name="parameter">the parameter to add</param>
        public void AddParameter(Parameter parameter)
        {
            if (this.parameters.Any(P => P.Id == parameter.Id))
            {
                throw new Exception(String.Format("Duplicate parameter ID: {0}", parameter.Id));
            }

            this.parameters.Add(parameter);
        }

        /// <summary>
        /// Load everything.
        /// </summary>
        public void LoadDatabase()
        {
            this.LoadStandardParameters();
            this.LoadRamParameters();
            this.LoadMathParameters();
            this.LoadBusParameters();
        }

        /// <summary>
        /// Load standard parameters.
        /// </summary>
        private void LoadStandardParameters()
        {
            string pathToXml = Path.Combine(this.pathToXmlDirectory, "Parameters.Standard.xml");
            XDocument xml = XDocument.Load(pathToXml);

            foreach (XElement parameterElement in xml.Root!.Elements("Parameter"))
            {
                var osElements = parameterElement.Elements("OS");
                List<uint> osids = new List<uint>();

                foreach (XElement os in osElements)
                {
                    string osidString = os.Attribute("id").Value;

                    if (osidString.ToLower() == "all")
                    {
                        osids.Clear(); //going to use no osids as all supported.
                        break;
                    }

                    uint osid = uint.Parse(osidString);
                        
                    osids.Add(osid);
                }

                List<Conversion> conversions = GetConversions(
                    parameterElement, parameterElement.Attribute("storageType").Value);

                PidParameter parameter = new PidParameter(
                    parameterElement.Attribute("id").Value,
                    parameterElement.Attribute("name").Value,
                    parameterElement.Attribute("description").Value,
                    parameterElement.Attribute("storageType").Value,
                    bool.Parse(parameterElement.Attribute("bitMapped").Value),
                    conversions,
                    UnsignedHex.GetUnsignedHex("0x" + parameterElement.Attribute("pid").Value),
                    osids);

                // SAE PIDs are the 00xx range; GM's enhanced set is everything above it. Two
                // categories to navigate by, but one source: both are requested the same way.
                parameter.Category = ReadCategory(
                    parameterElement, parameter.PID <= 0x00FF ? "SAE" : "GM Enhanced");
                parameter.Source = ReadSource(parameterElement, ParameterSources.GmEnhancedObd);

                AddParameter(parameter);
            }
        }

        /// <summary>
        /// Load RAM parameters.
        /// </summary>
        private void LoadRamParameters()
        {
            string pathToXml = Path.Combine(this.pathToXmlDirectory, "Parameters.RAM.xml");
            XDocument xml = XDocument.Load(pathToXml);

            foreach (XElement parameterElement in xml.Root!.Elements("RamParameter"))
            {
                Dictionary<uint, uint> addresses = new Dictionary<uint, uint>();
                foreach (XElement location in parameterElement.Elements("Location"))
                {
                    string osidString = location.Attribute("os").Value;
                    uint osid = uint.Parse(osidString);

                    string addressString = location.Attribute("address").Value;
                    uint address = UnsignedHex.GetUnsignedHex(addressString);

                    addresses[osid] = address;
                }

                List<Conversion> conversions = GetConversions(
                    parameterElement, parameterElement.Attribute("storageType").Value);

                RamParameter parameter = new RamParameter(
                    parameterElement.Attribute("id").Value,
                    parameterElement.Attribute("name").Value,
                    parameterElement.Attribute("description").Value,
                    parameterElement.Attribute("storageType").Value,
                    bool.Parse(parameterElement.Attribute("bitMapped").Value),
                    conversions,
                    addresses);

                parameter.Category = ReadCategory(parameterElement, "RAM");
                parameter.Source = ReadSource(parameterElement, ParameterSources.GmEnhancedObd);

                AddParameter(parameter);
            }
        }

        /// <summary>
        /// Load math parameters.
        /// </summary>
        private void LoadMathParameters()
        {
            string pathToXml = Path.Combine(this.pathToXmlDirectory, "Parameters.Math.xml");
            XDocument xml = XDocument.Load(pathToXml);
            foreach (XElement parameterElement in xml.Root!.Elements("MathParameter"))
            {
                string parameterName = parameterElement.Attribute("name").Value;

                // A math parameter has no storage of its own, so its range comes from the file or
                // from the data.
                List<Conversion> conversions = GetConversions(parameterElement, string.Empty);

                string xId = parameterElement.Attribute("xParameterId").Value;
                string xUnits = parameterElement.Attribute("xParameterConversion").Value;
                string yId = parameterElement.Attribute("yParameterId").Value;
                string yUnits = parameterElement.Attribute("yParameterConversion").Value;

                LogColumn xLogColumn = BuildLogColumnForMathParameter(xId, xUnits, parameterName);
                LogColumn yLogColumn = BuildLogColumnForMathParameter(yId, yUnits, parameterName);

                MathParameter parameter = new MathParameter(
                    parameterElement.Attribute("id").Value,
                    parameterName,
                    parameterElement.Attribute("description").Value,
                    conversions,
                    xLogColumn,
                    yLogColumn);

                parameter.Category = ReadCategory(parameterElement, "Math");
                parameter.Source = ReadSource(parameterElement, ParameterSources.Math);

                AddParameter(parameter);
            }
        }

        private void LoadBusParameters()
        {
            string pathToXml = Path.Combine(this.pathToXmlDirectory, "Parameters.AEM.xml");
            XDocument xml = XDocument.Load (pathToXml);
            foreach (XElement messageElement in xml.Root!.Elements("Message"))
            {
                string messageIdString = messageElement.Attribute("id").Value;
                UInt32 messageId = UInt32.Parse(messageIdString, NumberStyles.HexNumber);

                List<BusParameter> parameters = new List<BusParameter>();

                foreach (XElement parameterElement in messageElement.Elements("Parameter"))
                {
                    string id = parameterElement.Attribute("id").Value;
                    string name = parameterElement.Attribute("name").Value;
                    string description = parameterElement.Attribute("description").Value;

                    uint firstByte = uint.Parse(parameterElement.Attribute("firstByte").Value);
                    uint byteCount = uint.Parse(parameterElement.Attribute("byteCount").Value);
                    bool highByteFirst = bool.Parse(parameterElement.Attribute("highByteFirst").Value);
                    string aggregationString = parameterElement.Attribute("aggregation").Value;
                    Aggregation aggregation = (Aggregation)Enum.Parse(typeof(Aggregation), aggregationString);

                    // A broadcast parameter's width is its byte count, which gives it a scale the
                    // same way a PID's storage type does.
                    List<Conversion> conversions = GetConversions(
                        parameterElement, byteCount == 1 ? "uint8" : byteCount == 2 ? "uint16" : string.Empty);

                    BusParameter parameter = new BusParameter(
                        messageId, 
                        firstByte, 
                        byteCount, 
                        highByteFirst, 
                        id, 
                        name, 
                        description, 
                        conversions,
                        aggregation);

                    // Broadcast parameters are only meaningful when an auxiliary bus is being
                    // monitored, so they carry their own category and the grid can hide them until
                    // there is one.
                    parameter.Category = ReadCategory(parameterElement, "Auxiliary Bus");
                    parameter.Source = ReadSource(parameterElement, ParameterSources.Broadcast);

                    // Everything in this file is a vendor gauge that is only built for CAN. A VPW
                    // broadcast added here would carry bus="any".
                    parameter.RequiredBus = ReadRequiredBus(parameterElement, BusProtocol.Can500k);

                    parameters.Add(parameter);
                }

                this.busParameters[messageId] = parameters;
            }
        }

        private LogColumn BuildLogColumnForMathParameter(string id, string units, string parameterName)
        {
            Parameter? xParameter = this.parameters.Where(x => (x.Id == id)).FirstOrDefault();

            if (xParameter == null)
            {
                throw new Exception(String.Format("No parameter found for {0} in {1}", id, parameterName));
            }

            Conversion? xConversion = xParameter.Conversions.Where(x => x.Units == units).FirstOrDefault();

            if (xConversion == null)
            {
                throw new Exception(String.Format("No conversion found for {0} in {1}", units, parameterName));
            }

            return new LogColumn(xParameter, xConversion, false);
        }

        /// <summary>
        /// Read a parameter's conversions, giving each one a scale: the file's own rangeLow/rangeHigh
        /// if it states them, otherwise derived from the expression and the storage width.
        /// </summary>
        List<Conversion> GetConversions(XElement parameterElement, string storageType)
        {
            List<Conversion> returnConversions = new List<Conversion>();
            bool bitMapped = IsBitmapped(parameterElement);
            string parameterId = parameterElement.Attribute("id")?.Value ?? string.Empty;

            foreach (XElement conversionXml in parameterElement.Elements("Conversion"))
            {
                if (bitMapped)
                {
                    int bitIndex = Convert.ToInt32(parameterElement.Attribute("bitIndex")?.Value);
                    returnConversions.Add(CreateBooleanConversion(conversionXml, bitIndex));
                }
                else
                {
                    returnConversions.Add(
                        CreateNumericConversion(conversionXml, parameterId, storageType));
                }
            }

            return returnConversions;
        }

        private bool IsBitmapped(XElement parameterElement)
        {
            string? bitMappedAttributeValue = parameterElement.Attribute("bitMapped")?.Value;
            if (string.IsNullOrEmpty(bitMappedAttributeValue))
            {
                return false;
            }

            if (bool.TryParse(bitMappedAttributeValue, out bool result))
            {
                return result;
            }

            return false;
        }

        private Conversion CreateBooleanConversion(XElement conversionXml, int bitIndex)
        {
            string[] values = conversionXml.Attribute("expression").Value.Split(',');

            if (values.Length != 2)
            {
                throw new Exception("Boolean expression must have two values separated by a comma");
            }

            return new Conversion(
                conversionXml.Attribute("units").Value,
                bitIndex,
                values[0],
                values[1]);
        }

        private Conversion CreateNumericConversion(
            XElement conversionXml, string parameterId, string storageType)
        {
            string units = conversionXml.Attribute("units").Value;
            string expression = conversionXml.Attribute("expression").Value;
            string format = conversionXml.Attribute("format").Value;

            // An explicit range in the file wins, so a definition can correct a derived one.
            double low = ReadDouble(conversionXml, "rangeLow");
            double high = ReadDouble(conversionXml, "rangeHigh");

            if (high <= low)
            {
                (double Low, double High)? derived =
                    ConversionRange.Derive(parameterId, expression, storageType, bitMapped: false);

                if (derived != null)
                {
                    low = derived.Value.Low;
                    high = derived.Value.High;
                }
            }

            return new Conversion(units, expression, format, low, high);
        }

        private static double ReadDouble(XElement element, string attributeName)
        {
            string? text = element.Attribute(attributeName)?.Value;
            return double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
        }

        /// <summary>The parameter's category, or the supplied default when the file does not say.</summary>
        private static string ReadCategory(XElement element, string fallback)
        {
            string? category = element.Attribute("category")?.Value;
            return string.IsNullOrWhiteSpace(category) ? fallback : category!;
        }

        /// <summary>
        /// Which source supplies this parameter. Defaulted per definition file, so existing files
        /// need no change and only a parameter that breaks its file's pattern has to say so.
        /// </summary>
        private static string ReadSource(XElement element, string fallback)
        {
            string? source = element.Attribute("source")?.Value;
            return string.IsNullOrWhiteSpace(source) ? fallback : source!;
        }

        /// <summary>
        /// Which bus this parameter is confined to. Defaulted per definition file; "any" overrides a
        /// file whose default is a specific bus.
        /// </summary>
        private static BusProtocol? ReadRequiredBus(XElement element, BusProtocol? fallback)
        {
            string? bus = element.Attribute("bus")?.Value;

            if (string.IsNullOrWhiteSpace(bus))
            {
                return fallback;
            }

            if (string.Equals(bus, "any", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return (BusProtocol)Enum.Parse(typeof(BusProtocol), bus!, true);
        }
    }
}