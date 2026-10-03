// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// A time-stamped record of logged samples, held so that a monitor can be scrolled, zoomed and
    /// replayed independently of what is arriving from the vehicle.
    /// </summary>
    /// <remarks>
    /// Unbounded: a session keeps everything until it is cleared, so the limit is the machine's
    /// memory and how far the user chooses to push it. Nothing is ever dropped, which means sample
    /// index 0 is always the first sample of the session and an index taken now is still valid an
    /// hour later - a monitor cursor never has to be re-based.
    ///
    /// Storage is a chain of fixed-size chunks rather than one array. A single array would have to
    /// be reallocated and copied as the session grew, which at tens of megabytes is a long pause in
    /// the middle of logging; a chunk list grows by allocating one more block and copies nothing.
    /// Values are plain doubles rather than LogRowElement because the display string can be rebuilt
    /// from the value and the parameter's digit count whenever it is needed.
    /// </remarks>
    public sealed class LogHistory
    {
        /// <summary>
        /// Samples per chunk. Large enough that allocation is rare, small enough that a session with
        /// a wide profile does not allocate in huge steps.
        /// </summary>
        private const int ChunkSamples = 1024;

        private readonly string[] columnIds;
        private readonly Dictionary<string, int> columnIndexById;

        /// <summary>Each chunk holds ChunkSamples rows of ColumnCount values, laid out row by row.</summary>
        private readonly List<double[]> valueChunks = new List<double[]>();

        private readonly List<DateTime[]> timestampChunks = new List<DateTime[]>();

        public LogHistory(IEnumerable<string> columnIds)
        {
            if (columnIds == null)
            {
                throw new ArgumentNullException(nameof(columnIds));
            }

            this.columnIds = columnIds.ToArray();
            this.columnIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < this.columnIds.Length; index++)
            {
                // A duplicate id would otherwise throw; first one wins, which keeps the history
                // usable with an odd profile rather than refusing to log at all.
                if (!this.columnIndexById.ContainsKey(this.columnIds[index]))
                {
                    this.columnIndexById.Add(this.columnIds[index], index);
                }
            }
        }

        /// <summary>How many samples are held. Grows for the life of the session.</summary>
        public int Count { get; private set; }

        public IReadOnlyList<string> ColumnIds => this.columnIds;

        public int ColumnCount => this.columnIds.Length;

        /// <summary>Time from the first sample to the most recent.</summary>
        public TimeSpan Duration =>
            this.Count < 2 ? TimeSpan.Zero : this.GetTimestamp(this.Count - 1) - this.GetTimestamp(0);

        /// <summary>Roughly how much the samples occupy, for showing the user what a session costs.</summary>
        public long ApproximateBytes =>
            ((long)this.valueChunks.Count * ChunkSamples * Math.Max(1, this.ColumnCount) * sizeof(double))
            + ((long)this.timestampChunks.Count * ChunkSamples * 8);

        /// <summary>The column a parameter's values go into, or -1 if it is not in this history.</summary>
        public int IndexOfColumn(string columnId)
        {
            if (columnId != null && this.columnIndexById.TryGetValue(columnId, out int index))
            {
                return index;
            }

            return -1;
        }

        /// <summary>
        /// Add one sample. Values are positional and must match <see cref="ColumnCount"/>; a column
        /// with no reading should be NaN rather than omitted, so the row stays aligned.
        /// </summary>
        public void Append(DateTime timestamp, IReadOnlyList<double> sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            if (sample.Count != this.columnIds.Length)
            {
                throw new ArgumentException(
                    $"Expected {this.columnIds.Length} values, got {sample.Count}.", nameof(sample));
            }

            lock (this.sync)
            {
                int chunk = this.Count / ChunkSamples;
                int offset = this.Count % ChunkSamples;
                if (chunk == this.valueChunks.Count)
                {
                    this.valueChunks.Add(new double[ChunkSamples * Math.Max(1, this.ColumnCount)]);
                    this.timestampChunks.Add(new DateTime[ChunkSamples]);
                }

                double[] values = this.valueChunks[chunk];
                int row = offset * this.ColumnCount;
                for (int column = 0; column < sample.Count; column++)
                {
                    values[row + column] = sample[column];
                }

                this.timestampChunks[chunk][offset] = timestamp;

                // Last, so a reader that has snapshotted Count only ever sees complete rows.
                this.Count++;
            }
        }

        /// <summary>
        /// Add one sample from a logger row, matching by parameter id. Columns the row does not
        /// mention are recorded as NaN.
        /// </summary>
        public void Append(DateTime timestamp, IEnumerable<LogRowElement> row)
        {
            double[] sample = new double[this.columnIds.Length];
            for (int index = 0; index < sample.Length; index++)
            {
                sample[index] = double.NaN;
            }

            foreach (LogRowElement element in row ?? Enumerable.Empty<LogRowElement>())
            {
                int column = this.IndexOfColumn(element.ParameterId);
                if (column >= 0)
                {
                    sample[column] = element.ValueAsNumber;
                }
            }

            this.Append(timestamp, sample);
        }

        public DateTime GetTimestamp(int sampleIndex)
        {
            this.CheckRange(sampleIndex);
            return this.timestampChunks[sampleIndex / ChunkSamples][sampleIndex % ChunkSamples];
        }

        public double GetValue(int sampleIndex, int columnIndex)
        {
            this.CheckRange(sampleIndex);
            if (columnIndex < 0 || columnIndex >= this.columnIds.Length)
            {
                return double.NaN;
            }

            double[] values = this.valueChunks[sampleIndex / ChunkSamples];
            return values[((sampleIndex % ChunkSamples) * this.ColumnCount) + columnIndex];
        }

        /// <summary>
        /// The newest sample at or before the given time, or -1 when the session starts after it.
        /// Used to place the cursor from a click on the plot.
        /// </summary>
        public int IndexAtOrBefore(DateTime timestamp)
        {
            if (this.Count == 0 || this.GetTimestamp(0) > timestamp)
            {
                return -1;
            }

            // Samples are appended in time order, so this is a binary search.
            int low = 0;
            int high = this.Count - 1;
            while (low < high)
            {
                int middle = low + ((high - low + 1) / 2);
                if (this.GetTimestamp(middle) <= timestamp)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        /// <summary>Drop everything. The column layout is kept, so the history can be reused.</summary>
        public void Clear()
        {
            lock (this.sync)
            {
                this.Count = 0;
                this.valueChunks.Clear();
                this.timestampChunks.Clear();
            }
        }

        /// <summary>
        /// Guards the chunk lists. The logging thread appends while the UI reads, and appending can
        /// grow a List, which a reader must not index through mid-growth. Readers take it once per
        /// window rather than once per sample - see <see cref="ReadWindow"/>.
        /// </summary>
        private readonly object sync = new object();

        /// <summary>
        /// Copy a window of one column, newest-last, into <paramref name="values"/>, with the matching
        /// timestamps into <paramref name="timestamps"/>. Returns how many samples were copied, which
        /// is fewer than asked for when the window runs past the end.
        /// </summary>
        /// <remarks>
        /// A bulk read rather than a loop over <see cref="GetValue"/>: a chart redraw wants thousands
        /// of points, and this takes the lock once for all of them instead of once each. It is also
        /// the only read that is safe to make while logging - the per-sample accessors are for a
        /// history nothing is appending to.
        /// </remarks>
        public int ReadWindow(
            int columnIndex, int startSample, int count, double[] values, DateTime[]? timestamps)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            lock (this.sync)
            {
                if (columnIndex < 0 || columnIndex >= this.columnIds.Length || startSample < 0)
                {
                    return 0;
                }

                int available = Math.Min(count, this.Count - startSample);
                available = Math.Min(available, values.Length);
                if (timestamps != null)
                {
                    available = Math.Min(available, timestamps.Length);
                }

                for (int index = 0; index < available; index++)
                {
                    int sample = startSample + index;
                    int chunk = sample / ChunkSamples;
                    int offset = sample % ChunkSamples;

                    values[index] = this.valueChunks[chunk][(offset * this.ColumnCount) + columnIndex];
                    if (timestamps != null)
                    {
                        timestamps[index] = this.timestampChunks[chunk][offset];
                    }
                }

                return available < 0 ? 0 : available;
            }
        }

        /// <summary>
        /// The timestamp of a sample, or <see cref="DateTime.MinValue"/> when the index is outside
        /// what is held. Safe to call while logging, unlike <see cref="GetTimestamp"/>.
        /// </summary>
        public DateTime TimestampOrDefault(int sampleIndex)
        {
            lock (this.sync)
            {
                if (sampleIndex < 0 || sampleIndex >= this.Count)
                {
                    return DateTime.MinValue;
                }

                return this.timestampChunks[sampleIndex / ChunkSamples][sampleIndex % ChunkSamples];
            }
        }

        /// <summary>
        /// The value of a sample, or NaN when the index is outside what is held. Safe to call while
        /// logging, unlike <see cref="GetValue"/>.
        /// </summary>
        public double ValueOrNaN(int sampleIndex, int columnIndex)
        {
            lock (this.sync)
            {
                if (sampleIndex < 0 || sampleIndex >= this.Count
                    || columnIndex < 0 || columnIndex >= this.columnIds.Length)
                {
                    return double.NaN;
                }

                return this.valueChunks[sampleIndex / ChunkSamples]
                    [((sampleIndex % ChunkSamples) * this.ColumnCount) + columnIndex];
            }
        }

        private void CheckRange(int sampleIndex)
        {
            if (sampleIndex < 0 || sampleIndex >= this.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleIndex), $"{sampleIndex} is outside the {this.Count} samples held.");
            }
        }
    }
}
