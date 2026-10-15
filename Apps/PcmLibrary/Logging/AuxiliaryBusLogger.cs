// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Reads broadcast parameters from a second bus, which may be VPW or CAN.
    /// </summary>
    /// <remarks>
    /// This replaces a CAN-only logger that opened its own serial port at 2 Mbaud, picked its device
    /// through its own dialog and sniffed the bus for 1.5 s before every session. It is now an
    /// ordinary consumer of a <see cref="Device"/>, the same as <see cref="BusMonitor"/>: one
    /// interface selection serves it, and whichever bus that interface is on decides how frames
    /// arrive. Nothing here knows about ports or baud rates.
    ///
    /// The distinction that matters is not VPW against CAN - it is asking against listening. The PCM
    /// is polled for the parameters it is told to report; everything here is broadcast by other
    /// modules whether anyone listens or not, and is identified by the message it arrives in rather
    /// than by a PID. That is why this is a separate class from the PCM logger and not a mode of it.
    /// </remarks>
    public class AuxiliaryBusLogger : IDisposable
    {
        /// <summary>One parameter's value, converted and ready to display.</summary>
        public class ParameterAndValue
        {
            public BusParameter Parameter { get; private set; }
            public string Units { get; private set; }
            public string ValueAsString { get; private set; }
            public double ValueAsNumber { get; private set; }

            public ParameterAndValue(BusParameter parameter, string units, string valueAsString, double valueAsNumber)
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

        /// <summary>
        /// A VPW frame's first three bytes - priority, target, source - taken together as its
        /// identity, so one parameter model covers both buses.
        /// </summary>
        /// <remarks>
        /// A broadcast parameter is "this message, these bytes, this conversion" on either bus. CAN
        /// supplies an identifier directly; VPW's equivalent is its header, and the bytes a parameter
        /// addresses then start at the mode byte that follows it.
        /// </remarks>
        private const int VPWHeaderLength = 3;

        private readonly ParameterDatabase parameterDatabase;
        private readonly ILogger logger;

        /// <summary>The most recent value of each parameter, so a column always has something to show.</summary>
        private Dictionary<UInt32, Dictionary<string, ParameterAndValue>> snapshot =
            new Dictionary<UInt32, Dictionary<string, ParameterAndValue>>();

        private IEnumerable<UInt32> sortedMessageIds = Enumerable.Empty<UInt32>();

        private Dictionary<UInt32, IEnumerable<string>> sortedParameterIds =
            new Dictionary<UInt32, IEnumerable<string>>();

        /// <summary>
        /// Values received since the last row was built, awaiting aggregation. Touched by the read
        /// loop and by the logging thread, so only ever inside "lock (this.messages)".
        /// </summary>
        private Dictionary<UInt32, Dictionary<string, List<ParameterAndValue>>> messages =
            new Dictionary<UInt32, Dictionary<string, List<ParameterAndValue>>>();

        private Device? device;
        private BusProtocol protocol;
        private CancellationTokenSource? cancellation;
        private Task? readLoop;

        public AuxiliaryBusLogger(ParameterDatabase parameterDatabase, ILogger logger)
        {
            this.parameterDatabase = parameterDatabase;
            this.logger = logger;

            // Here rather than in Start, so the column set is known from construction and a caller
            // can build its log file header without waiting for an interface to open.
            this.UseDatabaseKeys();
        }

        /// <summary>Whether a bus is currently being read.</summary>
        public bool IsRunning => this.readLoop != null;

        /// <summary>
        /// Begin reading broadcast parameters from a device. Passing null stops, which is how the
        /// auxiliary bus is turned off.
        /// </summary>
        public async Task Start(Device? device, BusProtocol protocol)
        {
            await this.Stop();

            // Only the pending values: the columns were fixed at construction, and rebuilding them
            // here would race a logging thread already reading them.
            lock (this.messages)
            {
                this.messages.Clear();
            }

            if (device == null)
            {
                return;
            }

            if (protocol == BusProtocol.Can500k && !(device is IRawCanMonitor))
            {
                this.logger.AddUserMessage(
                    device.ToString() + " cannot stream raw CAN frames, so the auxiliary bus will not be read.");
                return;
            }

            if (!await device.BeginMonitor(protocol))
            {
                this.logger.AddUserMessage(
                    "Unable to monitor " + protocol + " on " + device.ToString() + ".");
                return;
            }

            this.device = device;
            this.protocol = protocol;
            this.cancellation = new CancellationTokenSource();

            CancellationToken token = this.cancellation.Token;
            this.readLoop = Task.Run(() => this.ReadLoop(token));

            this.logger.AddUserMessage("Reading the auxiliary bus: " + protocol + " on " + device.ToString() + ".");

            this.ReportParametersThisBusCannotCarry(protocol);
        }

        /// <summary>
        /// Name the parameters that cannot appear on the bus this interface landed on.
        /// </summary>
        /// <remarks>
        /// Reported rather than removed: the column set is fixed before the interface is opened, so
        /// the log file's header may already be written by the time the bus is known. A column that
        /// never updates with an explanation beats a header that does not match its rows.
        /// </remarks>
        private void ReportParametersThisBusCannotCarry(BusProtocol protocol)
        {
            string[] unavailable = this.parameterDatabase.BusParameters
                .Where(x => x.RequiredBus.HasValue && x.RequiredBus.Value != protocol)
                .Select(x => x.Name)
                .ToArray();

            if (unavailable.Length == 0)
            {
                return;
            }

            this.logger.AddUserMessage(
                $"These need a different bus and will not update on {protocol}: {string.Join(", ", unavailable)}.");
        }

        /// <summary>Stop reading, and let the device go back to normal filtering.</summary>
        public async Task Stop()
        {
            if (this.cancellation != null)
            {
                this.cancellation.Cancel();
            }

            Task? loop = this.readLoop;
            if (loop != null)
            {
                try
                {
                    await loop;
                }
                catch (Exception exception)
                {
                    this.logger.AddDebugMessage("Auxiliary bus reader stopped: " + exception.Message);
                }
            }

            this.readLoop = null;

            if (this.cancellation != null)
            {
                this.cancellation.Dispose();
                this.cancellation = null;
            }

            if (this.device != null)
            {
                try
                {
                    await this.device.EndMonitor();
                }
                catch (Exception exception)
                {
                    // The device may already be gone; nothing here is worth failing a shutdown over.
                    this.logger.AddDebugMessage("Unable to end auxiliary bus monitoring: " + exception.Message);
                }

                this.device = null;
            }
        }

        private async Task ReadLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (this.protocol == BusProtocol.Can500k)
                    {
                        IRawCanMonitor monitor = (IRawCanMonitor)this.device!;
                        (uint id, byte[] frame) = await monitor.ReceiveCanFrame();
                        if (frame.Length > 0)
                        {
                            this.Record(id, frame);
                        }

                        continue;
                    }

                    Message? message = await this.device!.ReceiveMessage();
                    if (message == null)
                    {
                        continue;
                    }

                    byte[] bytes = message.GetBytes();
                    if (bytes.Length <= VPWHeaderLength)
                    {
                        continue;
                    }

                    uint messageId =
                        ((uint)bytes[0] << 16) | ((uint)bytes[1] << 8) | bytes[2];

                    byte[] payload = new byte[bytes.Length - VPWHeaderLength];
                    Array.Copy(bytes, VPWHeaderLength, payload, 0, payload.Length);

                    this.Record(messageId, payload);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // A read failure on a bus nobody is driving should not end logging of the PCM.
                    this.logger.AddDebugMessage("Auxiliary bus read failed: " + exception.Message);
                    await Task.Delay(100, CancellationToken.None);
                }
            }
        }

        /// <summary>
        /// Decode one broadcast message and remember whatever it carries. Separate from the read
        /// loop so the decoding can be exercised without a bus.
        /// </summary>
        public void Record(uint messageId, byte[] payload)
        {
            foreach (ParameterAndValue value in this.Translate(messageId, payload))
            {
                lock (this.messages)
                {
                    if (!this.messages.TryGetValue(messageId, out Dictionary<string, List<ParameterAndValue>> parameters))
                    {
                        parameters = new Dictionary<string, List<ParameterAndValue>>();
                        this.messages[messageId] = parameters;
                    }

                    if (!parameters.TryGetValue(value.Parameter.Id, out List<ParameterAndValue> received))
                    {
                        received = new List<ParameterAndValue>();
                        parameters[value.Parameter.Id] = received;
                    }

                    received.Add(value);
                }
            }
        }

        /// <summary>
        /// The parameters a message carries, converted. A message the database does not describe is
        /// ignored: the columns are decided by the database, so a value nothing would ever read is
        /// only memory to hold on to.
        /// </summary>
        private IEnumerable<ParameterAndValue> Translate(uint messageId, byte[] payload)
        {
            IReadOnlyDictionary<UInt32, IEnumerable<BusParameter>> known =
                this.parameterDatabase.GetBusParameters();

            if (!known.TryGetValue(messageId, out IEnumerable<BusParameter> parameters))
            {
                yield break;
            }

            foreach (BusParameter parameter in parameters)
            {
                double valueAsNumber = ReadValue(parameter, payload);

                Conversion conversion = parameter.SelectedConversion ?? parameter.Conversions.First();
                ValueConverter.Convert(
                    valueAsNumber, parameter.Name, conversion, out valueAsNumber, out string valueAsString);

                yield return new ParameterAndValue(parameter, conversion.Units, valueAsString, valueAsNumber);
            }
        }

        /// <summary>The raw value a parameter addresses within a message, or zero if it runs off the end.</summary>
        private static double ReadValue(BusParameter parameter, byte[] payload)
        {
            int index = (int)parameter.ByteIndex;
            int count = (int)parameter.ByteCount;

            if (count == 0)
            {
                // An event: the message arriving is the whole of the information.
                return 0;
            }

            if (index < 0 || index + count > payload.Length)
            {
                return 0;
            }

            double value = 0;
            for (int offset = 0; offset < count; offset++)
            {
                int position = parameter.HighByteFirst ? index + offset : index + count - 1 - offset;
                value = (value * 256) + payload[position];
            }

            return value;
        }

        /// <summary>
        /// Take the column set from the parameter database, so a log has the same shape every run.
        /// </summary>
        /// <remarks>
        /// The logger this replaced sniffed the bus for 1.5 s before every session, which made the
        /// column set depend on what happened to be transmitting in that window.
        /// </remarks>
        public void UseDatabaseKeys()
        {
            this.snapshot = new Dictionary<UInt32, Dictionary<string, ParameterAndValue>>();

            lock (this.messages)
            {
                this.messages.Clear();
            }

            IReadOnlyDictionary<UInt32, IEnumerable<BusParameter>> parameters =
                this.parameterDatabase.GetBusParameters();

            foreach (UInt32 messageId in parameters.Keys)
            {
                Dictionary<string, ParameterAndValue> entry = new Dictionary<string, ParameterAndValue>();
                foreach (BusParameter parameter in parameters[messageId])
                {
                    entry[parameter.Id] = new ParameterAndValue(
                        parameter, parameter.SelectedConversion?.Units ?? string.Empty, "0", 0);
                }

                this.snapshot[messageId] = entry;
            }

            this.Sort();
        }

        /// <summary>
        /// Fix the order of messages and of the parameters within them, so the columns come out the
        /// same way every time.
        /// </summary>
        private void Sort()
        {
            this.sortedMessageIds = this.snapshot.Keys.ToList().ToImmutableSortedSet();
            this.sortedParameterIds = new Dictionary<uint, IEnumerable<string>>();

            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                this.sortedParameterIds[messageId] =
                    this.snapshot[messageId].Keys.ToList().ToImmutableSortedSet();
            }
        }

        public IEnumerable<string> GetParameterNames()
        {
            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                foreach (string parameterId in this.sortedParameterIds[messageId])
                {
                    ParameterAndValue pv = this.snapshot[messageId][parameterId];
                    yield return LogColumnHeading.For(pv.Parameter, pv.Units);
                }
            }
        }

        public IEnumerable<ParameterAndValue> GetParameterValues()
        {
            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                Dictionary<string, ParameterAndValue> cache = this.snapshot[messageId];
                foreach (string parameterId in this.sortedParameterIds[messageId])
                {
                    if (this.TryGetParameter(messageId, parameterId, out ParameterAndValue received))
                    {
                        cache[parameterId] = received;
                    }

                    yield return cache[parameterId];
                }
            }
        }

        public IEnumerable<LogRowElement> GetParameterValuesV2()
        {
            foreach (UInt32 messageId in this.sortedMessageIds)
            {
                Dictionary<string, ParameterAndValue> cache = this.snapshot[messageId];
                foreach (string parameterId in this.sortedParameterIds[messageId])
                {
                    if (this.TryGetParameter(messageId, parameterId, out ParameterAndValue received))
                    {
                        cache[parameterId] = received;
                    }

                    ParameterAndValue value = cache[parameterId];

                    yield return new LogRowElement(
                        value.Parameter.Id,
                        value.Parameter.Name,
                        value.Units,
                        value.ValueAsString,
                        value.ValueAsNumber);
                }
            }
        }

        /// <summary>
        /// Aggregate whatever arrived for one parameter since the last row, and consume it. False
        /// when nothing arrived, in which case the caller keeps showing the previous value.
        /// </summary>
        private bool TryGetParameter(uint messageId, string parameterId, out ParameterAndValue parameterAndValue)
        {
            lock (this.messages)
            {
                parameterAndValue = null!;

                if (!this.messages.TryGetValue(messageId, out Dictionary<string, List<ParameterAndValue>> parameters))
                {
                    return false;
                }

                if (!parameters.TryGetValue(parameterId, out List<ParameterAndValue> received))
                {
                    return false;
                }

                if (!TryAggregate(received, out parameterAndValue))
                {
                    return false;
                }

                received.Clear();
                return true;
            }
        }

        /// <summary>
        /// Combine the values received for one parameter since the last row, the way that parameter
        /// asks for - a wheel speed wants its latest value, a counter wants the sum.
        /// </summary>
        private static bool TryAggregate(List<ParameterAndValue> received, out ParameterAndValue parameterAndValue)
        {
            parameterAndValue = null!;

            if (received.Count == 0)
            {
                return false;
            }

            if (received.Count == 1)
            {
                parameterAndValue = received[0];
                return true;
            }

            ParameterAndValue first = received[0];
            double aggregated;

            switch (first.Parameter.Aggregation)
            {
                case Aggregation.Sum:
                    aggregated = received.Sum(x => x.ValueAsNumber);
                    break;

                case Aggregation.Average:
                    aggregated = received.Average(x => x.ValueAsNumber);
                    break;

                case Aggregation.Max:
                    aggregated = received.Max(x => x.ValueAsNumber);
                    break;

                default:
                case Aggregation.Last:
                    aggregated = received[received.Count - 1].ValueAsNumber;
                    break;
            }

            Conversion conversion = first.Parameter.SelectedConversion
                ?? (first.Parameter.Conversions.Any()
                    ? first.Parameter.Conversions.First()
                    : Conversion.DefaultConversion);

            parameterAndValue = new ParameterAndValue(
                first.Parameter, conversion.Units, aggregated.ToString(conversion.Format), aggregated);

            return true;
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
                // The device belongs to whoever opened it; this only stops reading from it.
                this.Stop().Wait(TimeSpan.FromSeconds(2));
            }
        }
    }
}
