// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PcmHacking
{
    /// <summary>
    /// Supplies an unlock key for a PCM whose security algorithm lives outside this app (40-bit
    /// seed/key, e.g. E92). Given the PCM type and the seed the PCM returned, return the key bytes,
    /// or null to abort. The host implements the prompt (CLI stdin, GUI dialog).
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancelled when the user aborts the operation. A prompt blocks the operation it was called from,
    /// so a host that can dismiss its prompt should watch this and return null, otherwise Cancel does
    /// nothing until the prompt is answered.
    /// </param>
    public delegate byte[]? SecurityKeyProvider(PcmType pcmType, byte[] seed, CancellationToken cancellationToken);

    /// <summary>
    /// Persists proven (PCM type + seed -> key) pairs so a seed that unlocked once auto-unlocks next
    /// time without re-prompting. Plain text, one entry per line: "<pcmType> <seedHex> <keyHex>".
    /// Stored next to the executable. The key algorithm is never stored or computed here - only pairs
    /// the PCM already accepted.
    /// </summary>
    public sealed class SecurityKeyStore
    {
        private readonly string path;
        private readonly Dictionary<string, byte[]> entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public SecurityKeyStore(string path)
        {
            this.path = path;
            this.Load();
        }

        /// <summary>
        /// Default store: security-keys.txt in the user's application data. Not beside the executable -
        /// an install under Program Files is not writable, and the save would fail silently.
        /// </summary>
        public static SecurityKeyStore Default()
            => new SecurityKeyStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PcmHammer",
                "security-keys.txt"));

        private static string Key(PcmType pcmType, byte[] seed) => pcmType + " " + seed.ToHex(string.Empty);

        /// <summary>The saved key for this PCM type + seed, or null if none.</summary>
        public byte[]? TryGet(PcmType pcmType, byte[] seed)
            => this.entries.TryGetValue(Key(pcmType, seed), out byte[]? key) ? key : null;

        /// <summary>
        /// Record a proven pair and persist it. Returns the reason persisting failed, or null on
        /// success, so the caller can tell the user the key will have to be entered again.
        /// </summary>
        public string? Save(PcmType pcmType, byte[] seed, byte[] key)
        {
            this.entries[Key(pcmType, seed)] = (byte[])key.Clone();
            return this.Flush();
        }

        /// <summary>Forget a pair (e.g. after the PCM rejected the key) and persist.</summary>
        public void Remove(PcmType pcmType, byte[] seed)
        {
            if (this.entries.Remove(Key(pcmType, seed)))
            {
                this.Flush();
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(this.path))
                {
                    return;
                }
                foreach (string line in File.ReadAllLines(this.path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    {
                        continue;
                    }
                    string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 3)
                    {
                        continue;
                    }
                    byte[]? key = Utility.TryParseHex(parts[2]);
                    if (key != null)
                    {
                        this.entries[parts[0] + " " + parts[1].ToUpperInvariant()] = key;
                    }
                }
            }
            catch (Exception)
            {
                // A corrupt or unreadable store is non-fatal; unlock falls back to prompting.
            }
        }

        /// <summary>
        /// Rewrite the store. Writes to a temp file and moves it into place so an interrupted save
        /// cannot leave a half-written file where the saved keys used to be, matching how PackageStore
        /// writes. Returns the failure message, or null on success.
        /// </summary>
        private string? Flush()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# PcmHammer saved security keys: <pcmType> <seedHex> <keyHex>");
                foreach (KeyValuePair<string, byte[]> entry in this.entries)
                {
                    sb.Append(entry.Key).Append(' ').AppendLine(entry.Value.ToHex(string.Empty));
                }

                string? directory = Path.GetDirectoryName(this.path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = this.path + ".tmp";
                File.WriteAllText(temp, sb.ToString());
                if (File.Exists(this.path)) File.Delete(this.path);
                File.Move(temp, this.path);
                return null;
            }
            catch (Exception exception)
            {
                // Not fatal: the unlock already succeeded, the key just will not be remembered.
                return exception.Message;
            }
        }

    }
}
