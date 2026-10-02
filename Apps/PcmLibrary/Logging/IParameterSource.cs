// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// How confident a source is that it can supply a parameter.
    /// </summary>
    /// <remarks>
    /// The distinction exists because asking and listening give answers of different strength. A
    /// module refusing a request is a fact. A broadcast identifier not appearing in a two second
    /// window is not - the engine may be off, the module asleep, or the message only sent under load.
    /// Flattening the two would delete working parameters from people's profiles.
    /// </remarks>
    public enum ParameterConfidence
    {
        /// <summary>Nobody has asked. The default, and what fail-soft is there to cope with.</summary>
        Unknown = 0,

        /// <summary>The module answered. Trustworthy.</summary>
        Confirmed,

        /// <summary>The module refused. Trustworthy.</summary>
        Refused,

        /// <summary>Seen on the bus. Trustworthy that it exists; says nothing about what is absent.</summary>
        Observed,

        /// <summary>
        /// Not seen within the listening window. NOT evidence of absence - report it as "not seen",
        /// never as "not supported".
        /// </summary>
        NotObserved,
    }

    /// <summary>What a probe found out about one parameter.</summary>
    public sealed class ParameterAvailability
    {
        public ParameterAvailability(
            string parameterId, ParameterConfidence confidence, string? note = null)
        {
            this.ParameterId = parameterId;
            this.Confidence = confidence;
            this.Note = note;
        }

        public string ParameterId { get; }

        public ParameterConfidence Confidence { get; }

        /// <summary>
        /// Context worth keeping, such as the value seen while scanning. Never a reason to exclude a
        /// parameter: a reading of zero or all-ones is what a real sensor reports with the engine
        /// off, and a scan is usually run on a stationary car.
        /// </summary>
        public string? Note { get; }

        public bool IsUsable =>
            this.Confidence != ParameterConfidence.Refused;
    }

    /// <summary>
    /// What a source is given to work with: one interface, on one bus, aimed at one module.
    /// </summary>
    public sealed class ParameterSourceContext
    {
        public ParameterSourceContext(
            Device device, BusProtocol bus, Vehicle? vehicle, Target target, uint osid, ILogger logger)
        {
            this.Device = device;
            this.Bus = bus;
            this.Vehicle = vehicle;
            this.Target = target;
            this.Osid = osid;
            this.Logger = logger;
        }

        public Device Device { get; }

        /// <summary>The bus this connection settled on, by detection or by being the only option.</summary>
        public BusProtocol Bus { get; }

        /// <summary>
        /// Present when this connection can ask as well as listen. Null for a listen-only setup,
        /// which is a normal state - an interface watching gauges has no module to talk to.
        /// </summary>
        public Vehicle? Vehicle { get; }

        /// <summary>Which module. Several sources can share a bus aimed at different ones.</summary>
        public Target Target { get; }

        /// <summary>The module's operating system, which is what scopes its parameter set.</summary>
        public uint Osid { get; }

        public ILogger Logger { get; }
    }

    /// <summary>
    /// One family of parameters, obtained one way, from one connection.
    /// </summary>
    /// <remarks>
    /// Polling and listening are the same interface deliberately. At the point that matters a log row
    /// needs the current value of each parameter, and the caller should not care whether that value
    /// arrived because it was asked for or because a module volunteered it. Keeping them apart is
    /// what produced a second logger with its own port, picker and settings.
    ///
    /// The difference between them is cadence, and two methods contain it: a listener accumulates in
    /// the background between reads, a poller does its exchange inside the read.
    ///
    /// The interface is value-level on purpose. Whatever happens underneath - one request, a decoded
    /// broadcast, three ISO-TP frames reassembled into a VIN - is private to the source. That is what
    /// lets a multi-message parameter exist without the API knowing it does.
    /// </remarks>
    public interface IParameterSource : IDisposable
    {
        /// <summary>Identifies this family, and matches a parameter's declared source.</summary>
        string Name { get; }

        /// <summary>
        /// Buses this source can work on. Empty means any, and the bus is then whatever the
        /// connection settled on. Listening sources name CAN, which is the one real constraint in
        /// this design.
        /// </summary>
        IReadOnlyList<BusProtocol> SupportedBuses { get; }

        /// <summary>Whether this source can supply a parameter on the connection it was given.</summary>
        bool CanProvide(Parameter parameter);

        /// <summary>
        /// Find out which of these can actually be supplied. A source that cannot probe returns
        /// Unknown for everything, which is a legitimate answer and breaks nothing.
        /// </summary>
        Task<IReadOnlyList<ParameterAvailability>> Probe(
            IReadOnlyList<Parameter> candidates, CancellationToken cancellationToken);

        /// <summary>
        /// Prepare to supply exactly these parameters. False means this source contributes nothing,
        /// which is not an error - the rest of the row still logs.
        /// </summary>
        Task<bool> Open(IReadOnlyList<LogColumn> columns, CancellationToken cancellationToken);

        /// <summary>Begin background accumulation. A poller does nothing here.</summary>
        Task Start(CancellationToken cancellationToken);

        /// <summary>
        /// Stop using the bus so something else can, and start again after. A source that only
        /// speaks when spoken to has nothing to do; one that asked a module to transmit unprompted
        /// has to tell it to stop, or it talks over whatever comes next.
        /// </summary>
        Task Suspend();

        Task Resume();

        /// <summary>
        /// The current value of everything this source opened with. A poller performs its exchange
        /// here; a listener returns and consumes what has accumulated since the last call.
        /// </summary>
        Task<IReadOnlyList<LogRowElement>> Read(CancellationToken cancellationToken);

        /// <summary>Column headings, in the order Read returns them.</summary>
        IEnumerable<string> GetColumnNames();

        /// <summary>Stop, and release anything claimed in Open.</summary>
        Task Close();
    }
}
