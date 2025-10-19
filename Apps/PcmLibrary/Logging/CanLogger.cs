using DynamicExpresso;
using PcmHacking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
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
        private ILogger logger;
        Dictionary<UInt32, Dictionary<string, ParameterAndValue>> snapshot = new Dictionary<UInt32, Dictionary<string, ParameterAndValue>>();
        IEnumerable<UInt32> sortedMessageIds;
        Dictionary<UInt32, IEnumerable<string>> sortedParameterIds;
        ParameterDatabase parameterDatabase;


        // Note that this is accessed by multiple threads, so it must only be used within "lock(messages)"
        Dictionary<UInt32, Dictionary<string, List<ParameterAndValue>>> messages = new Dictionary<UInt32, Dictionary<string, List<ParameterAndValue>>>();

        public CanLogger(ParameterDatabase parameterDatabase, ILogger logger)
        {
            this.parameterDatabase = parameterDatabase;
            this.logger = logger;
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
            this.snapshot.Clear();

            lock (this.messages)
            {
                this.messages.Clear();
            }

            if (this.canPort != null)
            {
                SerialPortConfiguration configuration = new SerialPortConfiguration();
                configuration.BaudRate = 2000000;
                configuration.Timeout = 50;
                configuration.DataReceived = this.DataReceived;
                await this.canPort.OpenAsync(configuration);

                // Discover what messages are available on the bus.
                //
                // The idea here to automatically add columns to the log if the devices
                // are present, and don't add them if the devices are not present. So,
                // we can add every known device to Parameters.CAN.xml and user will just
                // automatically get data from whatever devices are in their vehicles.
                //
                // This seemed like a better idea than using checkboxes like the PCM and
                // math parameters. I'm not entirely sure it really was a better idea.
                // It adds a lot of complexity to the code, and it adds a pause at the
                // start of every logging session.
                Thread.Sleep(1500);

                ISet<uint> knownIds = new HashSet<uint>(this.parameterDatabase.GetCanParameters().Keys);
                lock (this.messages)
                {
                    foreach (UInt32 key in this.messages.Keys)
                    {

                        // When troubleshooting the CAN parser & serial port code, it is
                        // helpful to see whether anything got mistaken for a valid message.
                        //
                        // This was also helpful to discover what's present on the CAN
                        // bus, but sniffing should be a dedicated feature of the app,
                        // not something that happens randomly when starting every log.
//                        if (!knownIds.Contains(key))
 //                       {
  //                          continue;
   //                     }

                        Dictionary<string, ParameterAndValue> entry = new Dictionary<string, ParameterAndValue>();
                        this.snapshot.Add(key, entry);

                        Dictionary<string, List<ParameterAndValue>> idsAndParameters = this.messages[key];
                        foreach (string id in idsAndParameters.Keys)
                        {
                            entry.Add(id, idsAndParameters[id].FirstOrDefault());
                        }
                    }
                }
                this.Sort();
            }
        }

        private void Sort()
        {
            // Sort the message IDs and the parameter IDs of each message, so that the
            // log columns will come out in the same order every time.
            this.sortedMessageIds = this.snapshot.Keys.ToList().ToImmutableSortedSet();
            this.sortedParameterIds = new Dictionary<uint, IEnumerable<string>>();
            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                Dictionary<string, ParameterAndValue> parameterIds = this.snapshot[messageId];
                IEnumerable<string> sortedIds = parameterIds.Keys.ToList().ToImmutableSortedSet();
                this.sortedParameterIds[messageId] = sortedIds;
            }
        }

        /// <summary>
        /// Created for testing, but might be preferable to sniffing during SetPort.
        /// </summary>
        public void UseDatabaseKeys()
        {
            IReadOnlyDictionary<UInt32, IEnumerable<CanParameter>> canParameters = this.parameterDatabase.GetCanParameters();
            foreach (UInt32 messageId in canParameters.Keys)
            {
                Dictionary<string, ParameterAndValue> temp = new Dictionary<string, ParameterAndValue>();
                foreach(CanParameter parameter in canParameters[messageId])
                {
                    temp[parameter.Id] = new ParameterAndValue(parameter, parameter.SelectedConversion.Units, "0", 0);
                }
                this.snapshot[messageId] = temp;
            }

            this.Sort();
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
                    new Conversion[0],
                    Aggregation.Last);

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
                            if ((int)parameter.ByteIndex < message.Payload.Length)
                            {
                                valueAsNumber = message.Payload[(int)parameter.ByteIndex];
                            }
                            else
                            {
                                valueAsNumber = 0;
                            }
                            break;

                        case 2:
                            if ((int)parameter.ByteIndex + 1 < message.Payload.Length)
                            {
                                if (parameter.HighByteFirst)
                                {
                                    valueAsNumber =
                                        (message.Payload[(int)parameter.ByteIndex] << 8) +
                                        message.Payload[(int)parameter.ByteIndex + 1];
                                }
                                else
                                {
                                    valueAsNumber =
                                        (message.Payload[(int)parameter.ByteIndex + 1] << 8) +
                                        message.Payload[(int)parameter.ByteIndex];
                                }
                            }
                            else
                            {
                                valueAsNumber = 0;
                            }
                            break;

                        case 3:
                            if ((int)parameter.ByteIndex + 2 < message.Payload.Length)
                            {
                                if (parameter.HighByteFirst)
                                {
                                    valueAsNumber = 
                                        (message.Payload[(int)parameter.ByteIndex] << 16) +
                                        (message.Payload[(int)parameter.ByteIndex + 1] << 8) +
                                        message.Payload[(int)parameter.ByteIndex + 2];
                                }
                                else
                                {
                                    valueAsNumber = 
                                        (message.Payload[(int)parameter.ByteIndex + 2] << 16) +
                                        (message.Payload[(int)parameter.ByteIndex + 1] << 8) +
                                        message.Payload[(int)parameter.ByteIndex];
                                }
                            }
                            else
                            {
                                valueAsNumber = 0;
                            }
                            break;

                        case 4:
                            if ((int)parameter.ByteIndex + 4 < message.Payload.Length)
                            {
                                if (parameter.HighByteFirst)
                                {
                                    valueAsNumber = 
                                        (message.Payload[(int)parameter.ByteIndex] << 24) +
                                        (message.Payload[(int)parameter.ByteIndex + 1] << 16) +
                                        (message.Payload[(int)parameter.ByteIndex + 2] << 8) +
                                        message.Payload[(int)parameter.ByteIndex + 3]; 
                                }
                                else
                                {
                                    valueAsNumber = 
                                        (message.Payload[(int)parameter.ByteIndex + 3] << 24) +
                                        (message.Payload[(int)parameter.ByteIndex + 2] << 16) +
                                        (message.Payload[(int)parameter.ByteIndex + 1] << 8) +
                                        message.Payload[(int)parameter.ByteIndex];
                                }
                            }
                            else
                            {
                                valueAsNumber = 0;
                            }
                            break;
                    }

                    Conversion conversion = parameter.SelectedConversion ?? parameter.Conversions.First();
                    ValueConverter.Convert(valueAsNumber, parameter.Name, conversion, out valueAsNumber, out valueAsString);

                    if (message.MessageId == 0x2050 && valueAsNumber > 0)
                    {
                        //Debugger.Break();
                    }

                    ParameterAndValue result = new ParameterAndValue(parameter, conversion.Units, valueAsString, valueAsNumber);
                    yield return result;
                }
            }
        }

        public IEnumerable<string> GetParameterNames()
        {
            if (this.sortedParameterIds == null)
            {
                yield break;
            }

            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                foreach (string parameterId in this.sortedParameterIds[messageId])
                {
                    ParameterAndValue pv = this.snapshot[messageId][parameterId];
                    string name = pv.Parameter.Name + "(" + pv.Units + ")";
                    yield return name;
                }
            }
        }

        public IEnumerable<ParameterAndValue> GetParameterValues()
        {
            if (this.sortedMessageIds == null)
            {
                yield break;
            }

            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                var parameterCacheForThisMessage = this.snapshot[messageId];
                foreach (string parameterId in sortedParameterIds[messageId])
                {
                    ParameterAndValue parameterAndValue;
                    if (this.TryGetParameter(messageId, parameterId, out parameterAndValue))
                    {
                        parameterCacheForThisMessage[parameterId] = parameterAndValue;
                        yield return parameterAndValue;
                    }
                    else
                    {
                        yield return parameterCacheForThisMessage[parameterId];
                    }
                }
            }
        }

        public IEnumerable<LogRowElement> GetParameterValuesV2()
        {
            if (this.sortedMessageIds == null)
            {
                yield break;
            }

            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                var parameterCacheForThisMessage = this.snapshot[messageId];
                foreach (string parameterId in sortedParameterIds[messageId])
                {
                    ParameterAndValue parameterAndValue;
                    if (this.TryGetParameter(messageId, parameterId, out parameterAndValue))
                    {
                        parameterCacheForThisMessage[parameterId] = parameterAndValue;
                    }
                    else
                    {
                        parameterAndValue = parameterCacheForThisMessage[parameterId];
                    }

                    yield return new LogRowElement(
                        parameterAndValue.Parameter.Id,
                        parameterAndValue.Parameter.Name,
                        parameterAndValue.Units,
                        parameterAndValue.ValueAsString,
                        parameterAndValue.ValueAsNumber);
                }
            }
        }


        private bool TryGetParameter(uint messageId, string parameterId, out ParameterAndValue parameterAndValue)
        {
            lock (this.messages)
            {
                Dictionary<string, List<ParameterAndValue>> parametersForThisMessage;
                if (this.messages.TryGetValue(messageId, out parametersForThisMessage))
                {
                    List<ParameterAndValue> receivedList;
                    if (parametersForThisMessage.TryGetValue(parameterId, out receivedList))
                    {
                        if (this.TryAggregate(receivedList, out parameterAndValue))
                        {
                            receivedList.Clear();
                            return true;
                        }
                        else
                        {
                            parameterAndValue = null;
                            return false;
                        }
                    }
                    else
                    {
                        parameterAndValue = null;
                        return false;
                    }
                }
                else
                {
                    parameterAndValue = null;
                    return false;
                }
            }
        }

        private bool TryAggregate(List<ParameterAndValue> receivedList, out ParameterAndValue parameterAndValue)
        {
            if (receivedList.Count == 0)
            {
                parameterAndValue = null;
                return false;
            }

            if (receivedList.Count == 1)
            {
                parameterAndValue = receivedList[0];
                return true;
            }

            ParameterAndValue pv = receivedList[0];
            double aggregated = 0;
            switch (pv.Parameter.Aggregation)
            {
                case Aggregation.Sum:
                    foreach (var parameter in receivedList)
                    {
                        aggregated += parameter.ValueAsNumber;
                    }
                    break;

                case Aggregation.Average:
                    int samples = 0;
                    foreach (var parameter in receivedList)
                    {
                        aggregated += parameter.ValueAsNumber;
                        samples++;
                    }
                    aggregated /= samples;
                    break;

                case Aggregation.Max:
                    aggregated = double.MinValue;
                    foreach (var parameter in receivedList)
                    {
                        if (parameter.ValueAsNumber > aggregated)
                        {
                            aggregated = parameter.ValueAsNumber;
                        }
                    }
                    break;

                default:
                case Aggregation.Last:
                    aggregated = receivedList[receivedList.Count - 1].ValueAsNumber;
                    break;
            }

            // Return the selected conversion, or if there are conversions use the first converion, else use Converison.DefaultConversion.
            Conversion conversion = pv.Parameter.SelectedConversion ??
                ((pv.Parameter.Conversions.Count() > 0) ? 
                    pv.Parameter.Conversions.First() :
                    Conversion.DefaultConversion);

            string valueAsString = aggregated.ToString(conversion.Format);
            
            parameterAndValue = new ParameterAndValue(pv.Parameter, conversion.Units, valueAsString, aggregated);
            return true;
        }
    }
}
