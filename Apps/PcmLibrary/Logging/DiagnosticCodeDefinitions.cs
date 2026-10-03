// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;

namespace PcmHacking
{
    /// <summary>
    /// Code to description, loaded from DiagnosticCodes.txt.
    /// </summary>
    /// <remarks>
    /// Data rather than code, for the same reason the parameter lists are: a description nobody has
    /// written yet should be one line in a text file, not a rebuild. A code with no entry is reported
    /// by number alone - a wrong description sends someone to replace the wrong part, so an absent
    /// one is the better failure.
    /// </remarks>
    public sealed class DiagnosticCodeDefinitions
    {
        public const string FileName = "DiagnosticCodes.txt";

        private readonly Dictionary<string, string> descriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Count => this.descriptions.Count;

        /// <summary>Read the definitions beside the application, or none if the file is absent.</summary>
        public static DiagnosticCodeDefinitions Load(string directory, ILogger? logger = null)
        {
            DiagnosticCodeDefinitions definitions = new DiagnosticCodeDefinitions();
            string path = Path.Combine(directory, FileName);

            if (!File.Exists(path))
            {
                logger?.AddDebugMessage($"No {FileName}; codes will be shown without descriptions.");
                return definitions;
            }

            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    definitions.Read(reader);
                }
            }
            catch (Exception exception)
            {
                // Descriptions are a convenience; reading codes must still work without them.
                logger?.AddDebugMessage($"Unable to read {FileName}: {exception.Message}");
            }

            return definitions;
        }

        public void Read(TextReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                // First comma only: a description may contain one, the code never does.
                int comma = trimmed.IndexOf(',');
                if (comma <= 0 || comma == trimmed.Length - 1)
                {
                    continue;
                }

                this.descriptions[trimmed.Substring(0, comma).Trim()] =
                    trimmed.Substring(comma + 1).Trim();
            }
        }

        public string? DescriptionFor(string code)
        {
            this.descriptions.TryGetValue(code, out string? description);
            return description;
        }

        /// <summary>Build a code from its reported bytes, with a description if one is known.</summary>
        public DiagnosticCode Describe(ushort raw, DiagnosticCodeKind kind) =>
            new DiagnosticCode(raw, kind, this.DescriptionFor(DiagnosticCode.Decode(raw)));
    }
}
