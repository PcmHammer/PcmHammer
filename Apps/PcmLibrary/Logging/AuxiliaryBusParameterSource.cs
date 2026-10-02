// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Parameters broadcast on a second bus by modules nobody asked: AEM, Plex, and the like.
    /// </summary>
    /// <remarks>
    /// An adapter over <see cref="AuxiliaryBusLogger"/>. Its columns come from the parameter
    /// database rather than from the profile, so <see cref="Open"/> ignores what it is handed - a
    /// broadcast parameter is present or absent according to what is on the bus, which the profile
    /// has no say in. Giving this source its own list is step 3 of the parameter-sources plan.
    /// </remarks>
    public sealed class AuxiliaryBusParameterSource : IParameterSource
    {
        private readonly AuxiliaryBusLogger busLogger;
        private readonly bool enabled;

        /// <param name="enabled">
        /// Whether an auxiliary bus is configured. Passed in rather than read from the bus logger,
        /// which may not have attached its interface yet - that happens in the background.
        /// </param>
        public AuxiliaryBusParameterSource(AuxiliaryBusLogger busLogger, bool enabled)
        {
            this.busLogger = busLogger;
            this.enabled = enabled;
        }

        public string Name => ParameterSources.Broadcast;

        /// <summary>
        /// Empty: the interface serving this bus may be on either protocol, and which one is decided
        /// when it is opened.
        /// </summary>
        public IReadOnlyList<BusProtocol> SupportedBuses => Array.Empty<BusProtocol>();

        public bool CanProvide(Parameter parameter) => parameter.Source == this.Name;

        /// <summary>
        /// Nothing is reported. A parameter not seen in a listening window is not evidence of
        /// absence - the module may be asleep, or only transmit under load.
        /// </summary>
        public Task<IReadOnlyList<ParameterAvailability>> Probe(
            IReadOnlyList<Parameter> candidates, CancellationToken cancellationToken)
        {
            IReadOnlyList<ParameterAvailability> unknown = candidates
                .Select(x => new ParameterAvailability(x.Id, ParameterConfidence.Unknown))
                .ToList();

            return Task.FromResult(unknown);
        }

        /// <summary>
        /// True when an auxiliary bus is configured and any broadcast parameter is defined. Without
        /// the enabled check, a PCM source that failed to open still left broadcast columns to
        /// publish, so a session that found nothing produced rows of nothing instead of an error.
        /// </summary>
        public Task<bool> Open(IReadOnlyList<LogColumn> columns, CancellationToken cancellationToken) =>
            Task.FromResult(this.enabled && this.busLogger.GetParameterNames().Any());

        /// <summary>Nothing: the read loop is started when the device is attached.</summary>
        public Task Start(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Nothing: this listens on its own interface, not the one being handed over.</summary>
        public Task Suspend() => Task.CompletedTask;

        public Task Resume() => Task.CompletedTask;

        public Task<IReadOnlyList<LogRowElement>> Read(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LogRowElement>>(this.busLogger.GetParameterValuesV2().ToList());

        public IEnumerable<string> GetColumnNames() => this.busLogger.GetParameterNames();

        /// <summary>
        /// Nothing: the bus logger outlives any one session, because the interface it reads through
        /// is reopened only when the user changes it.
        /// </summary>
        public Task Close() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
