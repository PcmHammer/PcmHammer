// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// Reads every identifier a module answers, not just the ones we have names for. Which identifier
    /// carries what is exactly the question a sweep is run to answer, so each answer is shown as raw
    /// bytes, as a big-endian uint32 where it is four bytes wide, and as text where it is printable.
    /// </summary>
    /// <remarks>
    /// Bus-agnostic: a VPW 0x3C block read and a GMLAN 0x1A DID read both come back through
    /// <see cref="IPcmCommands.ReadDataByIdentifier"/> as [mode, id, data...] for an answer and
    /// [0x7F, mode, nrc] for a refusal, which is all this needs to tell them apart. Read-only -
    /// neither service can change anything.
    /// </remarks>
    public static class IdentifierSweep
    {
        /// <summary>
        /// Probe every identifier from 0x00 to 0xFF and report the ones that answered.
        /// </summary>
        /// <param name="knownNames">
        /// Display names for the identifiers we already know, by id. Anything absent is reported as
        /// "unnamed", which is what a sweep is looking for.
        /// </param>
        public static async Task<List<string>> Run(
            IPcmCommands commands,
            IReadOnlyDictionary<byte, string> knownNames,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var lines = new List<string>();
            int refused = 0;

            for (int id = 0x00; id <= 0xFF; id++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    lines.Add("Sweep cancelled.");
                    break;
                }

                // Most of the range is unsupported and answers with a refusal, so say something every
                // so often; a silent sweep looks like a hang.
                if (id % 0x20 == 0)
                {
                    logger.AddUserMessage(string.Format("Sweeping 0x{0:X2}-0x{1:X2}...", id, id + 0x1F));
                }

                Response<byte[]> response = await commands.ReadDataByIdentifier((byte)id, cancellationToken);
                if (!TryGetData(response, (byte)id, out byte[] data))
                {
                    refused++;
                    continue;
                }

                var described = new List<string> { BitConverter.ToString(data).Replace("-", " ") };
                if (data.Length == 4)
                {
                    described.Add("uint32 " + ToUint32(data));
                }

                string text = ToText(data);
                if (text.Length > 0 && text.All(c => c >= ' ' && c <= '~'))
                {
                    described.Add("text \"" + text + "\"");
                }

                string name = knownNames.TryGetValue((byte)id, out string knownName) ? knownName : "unnamed";
                string line = string.Format("ID {0:X2} ({1}): {2}", id, name, string.Join("  |  ", described));
                lines.Add(line);
                logger.AddUserMessage(line);
            }

            string summary = string.Format("{0} identifier(s) answered, {1} did not.", lines.Count, refused);
            lines.Add(summary);
            logger.AddUserMessage(summary);
            return lines;
        }

        /// <summary>
        /// The data an identifier reported, or false when it refused or answered for something else.
        /// </summary>
        private static bool TryGetData(Response<byte[]> response, byte id, out byte[] data)
        {
            data = Array.Empty<byte>();
            byte[] bytes = response.Value ?? Array.Empty<byte>();
            if (response.Status != ResponseStatus.Success
                || bytes.Length < 3
                || bytes[0] == Mode.NegativeResponse
                || bytes[1] != id)
            {
                return false;
            }

            data = new byte[bytes.Length - 2];
            Buffer.BlockCopy(bytes, 2, data, 0, data.Length);
            return true;
        }

        private static uint ToUint32(byte[] data)
        {
            return (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
        }

        private static string ToText(byte[] data)
        {
            string text = Encoding.ASCII.GetString(data);
            int nul = text.IndexOf('\0');
            if (nul >= 0)
            {
                text = text.Substring(0, nul);
            }

            return text.Trim();
        }
    }
}
