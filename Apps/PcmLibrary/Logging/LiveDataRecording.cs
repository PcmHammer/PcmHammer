// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>A streaming CSV recording with bounded UI memory and no partially valid rows.</summary>
    public static class LiveDataRecording
    {
        public static async Task Run(IParameterSource source, IReadOnlyList<LogColumn> columns,
            TextWriter output, string sourceLabel, Action<IReadOnlyList<LogRowElement>> onSample,
            CancellationToken cancellationToken)
        {
            if (columns.Count == 0) throw new ArgumentException("Select at least one reading.", nameof(columns));
            string[]? layout = null;
            var clock = Stopwatch.StartNew();
            TimeSpan lastSample = clock.Elapsed;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool opened = await source.Open(columns, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!opened) throw new LogStartFailedException("None of the selected readings are available. See Results.");
                int count = source.GetColumnNames().Count();
                if (count == 0) throw new LogStartFailedException("The PCM did not supply any log columns.");
                await source.Start(cancellationToken);
                clock.Restart();
                lastSample = clock.Elapsed;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IReadOnlyList<LogRowElement> row = await source.Read(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Count == count)
                    {
                        string[] ids = row.Select(r => r.ParameterId + "\n" + r.Units).ToArray();
                        if (layout == null)
                        {
                            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                                throw new InvalidOperationException("The logging source returned duplicate columns.");
                            layout = ids;
                            await output.WriteLineAsync("Source,TimestampUTC,ElapsedSeconds," + string.Join(",",
                                row.Select(r => Csv(r.ParameterName + " (" + r.Units + ")"))));
                        }
                        else if (!layout.SequenceEqual(ids))
                            throw new InvalidOperationException("The logging columns changed. Recording stopped to protect column alignment.");

                        await output.WriteLineAsync(Csv(sourceLabel) + "," + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                            + "," + clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + ","
                            + string.Join(",", row.Select(r => double.IsNaN(r.ValueAsNumber) || double.IsInfinity(r.ValueAsNumber)
                                ? string.Empty : r.ValueAsNumber.ToString("R", CultureInfo.InvariantCulture))));
                        await output.FlushAsync();
                        onSample(row.ToArray());
                        lastSample = clock.Elapsed;
                    }
                    else if (clock.Elapsed - lastSample > TimeSpan.FromSeconds(15))
                        throw new TimeoutException("No complete sample for 15 seconds. Check the connection and selected readings.");
                    await Task.Delay(100, cancellationToken);
                }
            }
            finally
            {
                // For a streaming source this stops unsolicited data before anything else uses the adapter.
                try { await source.Suspend(); }
                finally { await source.Close(); }
            }
        }

        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
