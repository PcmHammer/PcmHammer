using PcmHacking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PcmHacking
{
    public class CanLogger : IDisposable
    {
        public class ParameterAndValue
        {
            public CanParameter Parameter { get; private set; }
            public string Units { get; private set; }
            public string ValueAsString { get; private set; }
            public double ValueAsNumber { get; private set; }

            public ParameterAndValue(CanParameter parameter, string units, string valueAsString, double valueAsNumber)
            {
                this.Parameter = parameter;
                this.Units = units;
                this.ValueAsString = valueAsString;
                this.ValueAsNumber = valueAsNumber;
            }

            public override string ToString()
            {
                return $"{this.Parameter.Name}, {this.ValueAsString} {this.Units}";
            }
        }

        private IPort canPort;
        private CanParser parser = new CanParser();
        List<UInt32> keySnapshot = new List<UInt32>();
        ParameterDatabase parameterDatabase;

        // Note that this is accessed by multiple threads, so it must only be used within "lock(messages)"
        Dictionary<UInt32, Dictionary<string, List<ParameterAndValue>>> messages = new Dictionary<UInt32, Dictionary<string, List<ParameterAndValue>>>();

        public CanLogger(ParameterDatabase parameterDatabase)
        {
            this.parameterDatabase = parameterDatabase;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.canPort?.Dispose();
            }
        }

        public async Task SetPort(IPort port)
        {
            this.canPort?.Dispose();
            this.canPort = port;

            // Remove all known messages
            this.keySnapshot.Clear();

            lock (this.messages)
            {
                this.messages.Clear();
            }

            if (this.canPort != null)
            {
                SerialPortConfiguration configuration = new SerialPortConfiguration();
                configuration.BaudRate = 2000000;
                configuration.DataReceived = this.DataReceived;
                await this.canPort.OpenAsync(configuration);

                // Discover what messages are available on the bus.
                Thread.Sleep(1500);
                lock (this.messages)
                {
                    foreach (UInt32 key in this.messages.Keys)
                    {
                        this.keySnapshot.Add(key);
                    }
                }

                this.keySnapshot.Sort();
            }
        }

        /// <summary>
        /// Created for testing, but might be preferable to sniffing during SetPort.
        /// </summary>
        public void UseDatabaseKeys()
        {
            IReadOnlyDictionary<UInt32, IEnumerable<CanParameter>> canParameters = this.parameterDatabase.GetCanParameters();
            foreach (UInt32 key in canParameters.Keys)
            {
                this.keySnapshot.Add(key);
            }
        }

        public void DataReceived(byte[] buffer, int bytesReceived)
        {
            for(int i = 0; i < bytesReceived; i++)
            {
                CanMessage message;
                if (this.parser.IsCompleteMessage(buffer[i], out message))
                {
                    IEnumerable<ParameterAndValue> results = this.TranslateValue(message);

                    lock (this.messages)
                    {
                        Dictionary<string, List<ParameterAndValue>> parameters;
                        if (!this.messages.TryGetValue(message.MessageId, out parameters))
                        {
                            parameters = new Dictionary<string, List<ParameterAndValue>>();
                            this.messages[message.MessageId] = parameters;
                        }

                        foreach (ParameterAndValue pv in results)
                        {
                            List<ParameterAndValue> list;
                            if (!parameters.TryGetValue(pv.Parameter.Id, out list))
                            {
                                list = new List<ParameterAndValue>();
                                parameters[pv.Parameter.Id] = list;
                            }

                            list.Add(pv);
                        }
                    }
                }
            }
        }

        private IEnumerable<ParameterAndValue> TranslateValue(CanMessage message)
        {
            IReadOnlyDictionary<UInt32, IEnumerable<CanParameter>> canParameters = this.parameterDatabase.GetCanParameters();
            IEnumerable<CanParameter> parameters;            
            
            if (!canParameters.TryGetValue(message.MessageId, out parameters))
            {
                string name = message.MessageId.ToString("X8");
                CanParameter placeholderParameter = new CanParameter(
                    message.MessageId,
                    0,
                    0,
                    true,
                    name,
                    name,
                    string.Empty,
                    new Conversion[0]);

                string valueAsString;
                ulong valueAsNumber = 0;
                
                for (int byteIndex = 0; byteIndex < message.Payload.Length; byteIndex++)
                {
                    valueAsNumber <<= 8;
                    valueAsNumber |= message.Payload[byteIndex];
                }

                if (message.Payload.Length > 0)
                {
                    valueAsString = valueAsNumber.ToString("X8");
                }
                else
                {
                    valueAsString = "Empty";
                }

                ParameterAndValue result = new ParameterAndValue(placeholderParameter, "raw", valueAsString, valueAsNumber);
                yield return result;
            }
            else
            {
                string valueAsString = String.Empty;
                double valueAsNumber = 0;

                foreach (CanParameter parameter in parameters)
                {                    
                    switch (parameter.ByteCount)
                    {
                        case 0:
                            valueAsString = "Event";
                            valueAsNumber = 0;
                            break;

                        case 1:
                            valueAsNumber = message.Payload[(int)parameter.ByteIndex];
                            break;

                        case 2:
                            if (parameter.HighByteFirst)
                            {
                                valueAsNumber = (message.Payload[(int)parameter.ByteIndex] << 8)
                                    + message.Payload[(int)parameter.ByteIndex + 1];
                            }
                            else
                            {
                                valueAsNumber = (message.Payload[(int)parameter.ByteIndex + 1] << 8)
                                    + message.Payload[(int)parameter.ByteIndex];
                            }
                            break;

                        case 3:
                            if (parameter.HighByteFirst)
                            {
                                valueAsNumber = (message.Payload[(int)parameter.ByteIndex] << 16)
                                    + (message.Payload[(int)parameter.ByteIndex + 1] << 8)
                                    + message.Payload[(int)parameter.ByteIndex + 2];
                            }
                            else
                            {
                                valueAsNumber = (message.Payload[(int)parameter.ByteIndex + 2] << 16)
                                    + (message.Payload[(int)parameter.ByteIndex + 1] << 8)
                                    + message.Payload[(int)parameter.ByteIndex];
                            }
                            break;

                        case 4:
                            if (parameter.HighByteFirst)
                            {
                                valueAsNumber = (message.Payload[(int)parameter.ByteIndex] << 24) +
                                    + (message.Payload[(int)parameter.ByteIndex + 1] << 16) +
                                    + (message.Payload[(int)parameter.ByteIndex + 2] << 8) +
                                    + message.Payload[(int)parameter.ByteIndex + 3];
                            }
                            else
                            {
                                valueAsNumber = (message.Payload[(int)parameter.ByteIndex + 3] << 24) +
                                    + (message.Payload[(int)parameter.ByteIndex + 2] << 16) +
                                    + (message.Payload[(int)parameter.ByteIndex + 1] << 8) +
                                    + message.Payload[(int)parameter.ByteIndex];
                            }
                            break;
                    }

                    Conversion conversion = parameter.SelectedConversion ?? parameter.Conversions.First();
                    double convertedValue = 0;
                    string formattedValue;
                    ValueConverter.Convert(valueAsNumber, parameter.Name, conversion, out convertedValue, out formattedValue);

                    ParameterAndValue result = new ParameterAndValue(parameter, conversion.Units, valueAsString, valueAsNumber);
                    yield return result;
                }
            }
        }

        public IEnumerable<string> GetParameterNames()
        {
            foreach (UInt32 key in this.keySnapshot)
            {
                string name;
                lock(this.messages)
                {
                    var parametersInThisMessage = this.messages[key];
                    foreach (var parameterId in parametersInThisMessage.Keys)
                    {
                        var list = parametersInThisMessage[parameterId];
                        foreach (var pv in list)
                        {
                            name = pv.Parameter.Name + "(" + pv.Units + ")";
                            yield return name;
                        }
                    }
                }                
            }
        }

        public IEnumerable<ParameterAndValue> GetParameterValues()
        {
            foreach(UInt32 key in this.keySnapshot)
            {
                lock(this.messages)
                {
                    Dictionary<string, List<ParameterAndValue>> parametersInThisMessage;
                    if (this.messages.TryGetValue(key, out parametersInThisMessage))
                    {
                        foreach (var parameterId in parametersInThisMessage.Keys)
                        {
                            // TODO: this should be a list of values that were received with this ID
                            // They should be aggregated before returning (average or sum or last-one-wins).
                            var list = parametersInThisMessage[parameterId];
                            foreach (var pv in list)
                            {
                                yield return pv;
                            }
                        }
                    }
                    else
                    {
                        // TODO: return default values for parameters that were not updated since the last call
                        // Will require changing keySnapshot to something like:
                        // Dictionary<messageId, IEnumerable<parameterId, CanParameter>>
                    }
                }
            }
        }
    }
}
