// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// One column of a loaded log.
    /// </summary>
    public sealed class LoggedColumn
    {
        public LoggedColumn(string heading, string? address, string name, string units)
        {
            this.Heading = heading;
            this.Address = address;
            this.Name = name;
            this.Units = units;
        }

        /// <summary>The heading exactly as it appeared in the file.</summary>
        public string Heading { get; }

        /// <summary>"PID 000C", "MSG 000A0301", or null for a column with no address.</summary>
        public string? Address { get; }

        public string Name { get; }

        public string Units { get; }

        public override string ToString() => this.Heading;
    }

    /// <summary>What a log file contained.</summary>
    public sealed class LoggedData
    {
        public LoggedData(
            IReadOnlyList<LoggedColumn> columns,
            IReadOnlyList<DateTime> timestamps,
            IReadOnlyList<double[]> samples,
            int malformedRows)
        {
            this.Columns = columns;
            this.Timestamps = timestamps;
            this.Samples = samples;
            this.MalformedRows = malformedRows;
        }

        public IReadOnlyList<LoggedColumn> Columns { get; }

        public IReadOnlyList<DateTime> Timestamps { get; }

        /// <summary>One array per row, one value per column. NaN where a field would not parse.</summary>
        public IReadOnlyList<double[]> Samples { get; }

        /// <summary>Rows whose field count did not match the header, and were skipped.</summary>
        public int MalformedRows { get; }
    }

    /// <summary>
    /// Reads the .csv files written by <see cref="LogFileWriter"/>.
    /// </summary>
    /// <remarks>
    /// Fields are split on commas and trimmed, which is what the writer's "join with comma-space"
    /// assumes: neither end quotes anything, so a value containing a comma would already have
    /// corrupted the file on the way out. Keeping the two consistent is better than a reader that
    /// copes with files this program cannot produce.
    /// </remarks>
    public static class LogFileReader
    {
        /// <summary>The two columns LogFileWriter puts in front of the data.</summary>
        private const int LeadingColumns = 2;

        public static LoggedData Read(TextReader reader)
        {
            string? headerLine = reader.ReadLine();
            if (headerLine == null)
            {
                throw new InvalidDataException("The log file is empty.");
            }

            string[] headings = Split(headerLine);
            if (headings.Length <= LeadingColumns)
            {
                throw new InvalidDataException("The log file has no data columns.");
            }

            List<LoggedColumn> columns = headings
                .Skip(LeadingColumns)
                .Select(ParseHeading)
                .ToList();

            List<DateTime> timestamps = new List<DateTime>();
            List<double[]> samples = new List<double[]>();
            int malformed = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                string[] fields = Split(line);
                if (fields.Length != headings.Length)
                {
                    malformed++;
                    continue;
                }

                timestamps.Add(ParseTimestamp(fields[0], timestamps.Count));

                double[] sample = new double[columns.Count];
                for (int i = 0; i < columns.Count; i++)
                {
                    sample[i] = double.TryParse(
                        fields[i + LeadingColumns],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double value) ? value : double.NaN;
                }

                samples.Add(sample);
            }

            return new LoggedData(columns, timestamps, samples, malformed);
        }

        /// <summary>
        /// Split "PID 000C: Engine Speed (RPM)" into its address, name and units.
        /// </summary>
        public static LoggedColumn ParseHeading(string heading)
        {
            string remainder = heading;
            string? address = null;

            int colon = remainder.IndexOf(':');
            if (colon > 0)
            {
                address = remainder.Substring(0, colon).Trim();
                remainder = remainder.Substring(colon + 1).Trim();
            }

            string name = remainder;
            string units = string.Empty;

            // Units are the last parenthesised group, so a name containing brackets survives.
            int open = remainder.LastIndexOf('(');
            if (open > 0 && remainder.EndsWith(")", StringComparison.Ordinal))
            {
                name = remainder.Substring(0, open).Trim();
                units = remainder.Substring(open + 1, remainder.Length - open - 2).Trim();
            }

            return new LoggedColumn(heading, address, name, units);
        }

        private static DateTime ParseTimestamp(string field, int rowIndex)
        {
            // "u" format, as written. A file hand-edited into something else still loads: the
            // samples matter more than their clock, and a row index keeps them in order.
            if (DateTime.TryParse(
                    field,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime parsed))
            {
                return parsed;
            }

            return DateTime.MinValue.AddMilliseconds(rowIndex);
        }

        private static string[] Split(string line)
        {
            string[] fields = line.Split(',');
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = fields[i].Trim();
            }

            return fields;
        }
    }
}
