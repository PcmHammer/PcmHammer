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
    /// Reads and formats the GMLAN/CAN PCM properties via ReadDataByIdentifier.
    /// </summary>
    public static class CanProperties
    {
        private static readonly byte[] Dids = { 0x90, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC9, 0xCA, 0xCB, 0xB4 };

        public static async Task<List<string>> Read(CanCommands commands, CancellationToken cancellationToken)
        {
            Dictionary<byte, byte[]> responses = new Dictionary<byte, byte[]>();
            foreach (byte did in Dids)
            {
                if (cancellationToken.IsCancellationRequested) break;
                Response<byte[]> response = await commands.ReadDataByIdentifier(did, cancellationToken);
                if (response.Status == ResponseStatus.Success && response.Value.Length > 0)
                {
                    responses[did] = response.Value;
                }
            }

            return new List<string>
            {
                "OSID: " + Hex(responses, 0xC9),
                "VIN: " + Ascii(responses, 0x90, 0),
                "Serial: " + Ascii(responses, 0xB4, 0),
                "Module 1: " + Hex(responses, 0xCA),
                "Module 2: " + Hex(responses, 0xCB),
                "Module 3: " + Hex(responses, 0xC1),
                "Module 4: " + Hex(responses, 0xC2),
                "Module 5: " + Hex(responses, 0xC3),
                "Module 6: " + Hex(responses, 0xC4),
                "Module 7: " + Hex(responses, 0xC5),
                "Module 8: " + Hex(responses, 0xC6),
            };
        }

        // resp is the full response: [0x5A, did, data...] or [0x7F, 0x1A, nrc].
        private static string Ascii(Dictionary<byte, byte[]> responses, byte did, int skipBytes)
        {
            if (!responses.TryGetValue(did, out byte[] resp))
            {
                return "<no response>";
            }
            if (resp.Length >= 1 && resp[0] == Gmlan.NegativeResponse)
            {
                return resp.Length >= 3 ? string.Format("NRC 0x{0:X2}", resp[2]) : "NRC";
            }

            int start = 2 + skipBytes;   // skip [5A did] and the leading data bytes
            if (resp.Length <= start)
            {
                return "";
            }
            string text = Encoding.ASCII.GetString(resp.Skip(start).ToArray());
            int nul = text.IndexOf('\0');
            if (nul >= 0)
            {
                text = text.Substring(0, nul);
            }
            return text.Trim();
        }

        private static string Hex(Dictionary<byte, byte[]> responses, byte did)
        {
            if (!responses.TryGetValue(did, out byte[] resp))
            {
                return "<no response>";
            }
            if (resp.Length >= 1 && resp[0] == Gmlan.NegativeResponse)
            {
                return resp.Length >= 3 ? string.Format("NRC 0x{0:X2}", resp[2]) : "NRC";
            }

            // [5A did b3 b2 b1 b0] -> big-endian uint32, shown as decimal.
            if (resp.Length >= 6)
            {
                uint value = (uint)((resp[2] << 24) | (resp[3] << 16) | (resp[4] << 8) | resp[5]);
                return value.ToString();
            }
            return BitConverter.ToString(resp).Replace("-", " ");
        }
    }
}
