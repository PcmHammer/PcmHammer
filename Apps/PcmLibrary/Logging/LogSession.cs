// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// One logging session: the sources it draws from, and the rows they produce between them.
    /// </summary>
    /// <remarks>
    /// Composition used to live in <see cref="Logger"/>, which knew about the auxiliary bus and
    /// concatenated its values onto its own. That made the PCM logger the place every new kind of
    /// parameter had to be added to, which is what this exists to stop.
    ///
    /// Source order is the column order, in the header and in every row. It is fixed at construction
    /// and never re-derived, because the log file's header is written once and the rows have to keep
    /// matching it for the rest of the session.
    /// </remarks>
    public sealed class LogSession : IDisposable
    {
        private readonly IReadOnlyList<IParameterSource> sources;
        private readonly List<IParameterSource> open = new List<IParameterSource>();

        /// <summary>
        /// How many values each open source owes per row, fixed when it was opened. A source that
        /// returns a different number has produced a row that would line up against the wrong
        /// columns, which matters more than the row being late.
        /// </summary>
        private readonly Dictionary<IParameterSource, int> columnCounts =
            new Dictionary<IParameterSource, int>();

        public LogSession(IEnumerable<IParameterSource> sources)
        {
            this.sources = sources.ToList();
        }

        public IReadOnlyList<IParameterSource> Sources => this.sources;

        /// <summary>
        /// Parameters a module refused, from every source that reports any. Empty unless the profile
        /// asked for something this operating system lacks.
        /// </summary>
        public IReadOnlyList<Parameter> UnsupportedParameters =>
            this.sources.OfType<PcmParameterSource>().SelectMany(x => x.UnsupportedParameters).ToList();

        /// <summary>
        /// Prepare every source, each with the columns it says it can supply. False when none of
        /// them can contribute anything, which is the only case with no row to produce.
        /// </summary>
        public async Task<bool> Open(IReadOnlyList<LogColumn> columns, CancellationToken cancellationToken)
        {
            this.open.Clear();
            this.columnCounts.Clear();

            foreach (IParameterSource source in this.sources)
            {
                IReadOnlyList<LogColumn> mine = columns.Where(x => source.CanProvide(x.Parameter)).ToList();

                if (!await source.Open(mine, cancellationToken))
                {
                    continue;
                }

                int count = source.GetColumnNames().Count();
                if (count == 0)
                {
                    // Opened, but has nothing to say - an auxiliary bus with no parameters defined
                    // for it. Keeping it would make every row look incomplete.
                    continue;
                }

                this.open.Add(source);
                this.columnCounts[source] = count;
            }

            return this.open.Count > 0;
        }

        public async Task Start(CancellationToken cancellationToken)
        {
            foreach (IParameterSource source in this.open)
            {
                await source.Start(cancellationToken);
            }
        }

        /// <summary>Let go of the bus, and take it back when the other operation is done.</summary>
        public async Task Suspend()
        {
            foreach (IParameterSource source in this.open)
            {
                await source.Suspend();
            }
        }

        public async Task Resume()
        {
            foreach (IParameterSource source in this.open)
            {
                await source.Resume();
            }
        }

        public IEnumerable<string> GetColumnNames() =>
            this.open.SelectMany(x => x.GetColumnNames());

        /// <summary>
        /// One row: every open source's current values, in source order.
        /// </summary>
        /// <remarks>
        /// A source that owes values and does not produce them - a PCM that did not answer in time -
        /// abandons the row. The caller treats an empty row as nothing to display and asks again,
        /// which is better than publishing values against the wrong columns.
        /// </remarks>
        public async Task<IReadOnlyList<LogRowElement>> Read(CancellationToken cancellationToken)
        {
            List<LogRowElement> row = new List<LogRowElement>();

            foreach (IParameterSource source in this.open)
            {
                IReadOnlyList<LogRowElement> values = await source.Read(cancellationToken);

                if (values.Count != this.columnCounts[source])
                {
                    return Array.Empty<LogRowElement>();
                }

                row.AddRange(values);
            }

            return row;
        }

        public async Task Close()
        {
            foreach (IParameterSource source in this.open)
            {
                await source.Close();
            }

            this.open.Clear();
            this.columnCounts.Clear();
        }

        public void Dispose()
        {
            this.Close().Wait(TimeSpan.FromSeconds(2));

            foreach (IParameterSource source in this.sources)
            {
                source.Dispose();
            }
        }
    }
}
