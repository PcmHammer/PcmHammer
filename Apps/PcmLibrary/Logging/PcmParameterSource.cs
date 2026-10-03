// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Parameters the PCM is asked for: PIDs, RAM addresses, and the math columns derived from them.
    /// </summary>
    /// <remarks>
    /// An adapter over <see cref="Logger"/>, which keeps the three request strategies - DPID
    /// streaming, single-row DPIDs, and CAN polling. Which one runs is decided by the device and the
    /// bus, inside <see cref="Logger.Create"/>, and is no business of anything above this.
    ///
    /// Math lives here rather than in a source of its own because its values are computed from this
    /// source's values in the same row. Separating it needs a way to feed one source's output to
    /// another, which nothing yet asks for.
    /// </remarks>
    public sealed class PcmParameterSource : IParameterSource
    {
        private readonly Vehicle vehicle;
        private readonly uint osid;
        private readonly ILogger uiLogger;

        private Logger? logger;

        public PcmParameterSource(Vehicle vehicle, uint osid, ILogger uiLogger)
        {
            this.vehicle = vehicle;
            this.osid = osid;
            this.uiLogger = uiLogger;
        }

        public string Name => ParameterSources.GmEnhancedObd;

        /// <summary>Empty: asking works on whichever bus the module was found on.</summary>
        public IReadOnlyList<BusProtocol> SupportedBuses => Array.Empty<BusProtocol>();

        /// <summary>The logger behind this source, once opened. For UI that needs the row layout.</summary>
        public Logger? Logger => this.logger;

        public IReadOnlyList<Parameter> UnsupportedParameters =>
            this.logger?.UnsupportedParameters ?? (IReadOnlyList<Parameter>)Array.Empty<Parameter>();

        /// <summary>
        /// Its own source, and Math, whose values are computed from this source's in the same row.
        /// </summary>
        public bool CanProvide(Parameter parameter) =>
            parameter.Source == this.Name || parameter.Source == ParameterSources.Math;

        /// <summary>
        /// Nothing is known in advance. Asking the PCM is what finds out, and
        /// <see cref="Open"/> already does that for the parameters actually wanted.
        /// </summary>
        public Task<IReadOnlyList<ParameterAvailability>> Probe(
            IReadOnlyList<Parameter> candidates, CancellationToken cancellationToken)
        {
            IReadOnlyList<ParameterAvailability> unknown = candidates
                .Select(x => new ParameterAvailability(x.Id, ParameterConfidence.Unknown))
                .ToList();

            return Task.FromResult(unknown);
        }

        public async Task<bool> Open(IReadOnlyList<LogColumn> columns, CancellationToken cancellationToken)
        {
            await this.Close();

            if (columns.Count == 0)
            {
                return false;
            }

            this.logger = this.vehicle.CreateLogger(this.osid, columns, this.uiLogger);
            return await this.logger.StartLogging();
        }

        /// <summary>Nothing: a poller does its work inside Read.</summary>
        public Task Start(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Suspend() => this.logger?.Suspend() ?? Task.CompletedTask;

        public Task Resume() => this.logger?.Resume() ?? Task.CompletedTask;

        public async Task<IReadOnlyList<LogRowElement>> Read(CancellationToken cancellationToken)
        {
            if (this.logger == null)
            {
                return Array.Empty<LogRowElement>();
            }

            return (await this.logger.GetNextRowV2()).ToList();
        }

        public IEnumerable<string> GetColumnNames() =>
            this.logger?.GetColumnNames() ?? Enumerable.Empty<string>();

        public Task Close()
        {
            this.logger = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            this.Close().Wait(TimeSpan.FromSeconds(1));
        }
    }
}
