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
    /// Reads and formats the GMLAN/CAN PCM identification via ReadDataByIdentifier (service 0x1A).
    /// </summary>
    /// <remarks>
    /// DIDs use their GMLAN names: 0x90 VIN, 0xB4 Manufacturing Traceability Characters,
    /// 0xC1..0xCA Software Module Identifier (SWMI) 01..10 - each a flashed software/calibration
    /// segment's part number - and 0xCB End Model Part Number. DIDs that answer with a negative
    /// response (not supported on this module) are omitted from the output.
    /// </remarks>
    public static class CanIdentification
    {
        private enum Format { Ascii, Uint32 }

        private struct Identifier
        {
            public byte Did;
            public string Name;
            public Format Format;
        }

        // Probed in DID order; the output lists whichever answered, in this order.
        private static readonly Identifier[] Identifiers =
        {
            new Identifier { Did = 0x90, Name = "VIN",                Format = Format.Ascii },
            new Identifier { Did = 0xB4, Name = "Traceability (MTC)", Format = Format.Ascii },
            new Identifier { Did = 0xC1, Name = "SWMI 01",            Format = Format.Uint32 },
            new Identifier { Did = 0xC2, Name = "SWMI 02",            Format = Format.Uint32 },
            new Identifier { Did = 0xC3, Name = "SWMI 03",            Format = Format.Uint32 },
            new Identifier { Did = 0xC4, Name = "SWMI 04",            Format = Format.Uint32 },
            new Identifier { Did = 0xC5, Name = "SWMI 05",            Format = Format.Uint32 },
            new Identifier { Did = 0xC6, Name = "SWMI 06",            Format = Format.Uint32 },
            new Identifier { Did = 0xC9, Name = "SWMI 09",            Format = Format.Uint32 },
            new Identifier { Did = 0xCA, Name = "SWMI 10",            Format = Format.Uint32 },
            new Identifier { Did = 0xCB, Name = "End Model P/N",      Format = Format.Uint32 },
        };

        public static async Task<List<string>> Read(CanCommands commands, CancellationToken cancellationToken)
        {
            // Positive responses only ([0x5A, did, data...]); NRCs are dropped here so the rest of the
            // method never has to test for them.
            Dictionary<byte, byte[]> responses = new Dictionary<byte, byte[]>();
            foreach (Identifier id in Identifiers)
            {
                if (cancellationToken.IsCancellationRequested) break;
                Response<byte[]> response = await commands.ReadDataByIdentifier(id.Did, cancellationToken);
                if (response.Status == ResponseStatus.Success
                    && response.Value.Length >= 2
                    && response.Value[0] == Gmlan.ReadDataByIdentifierResponse)
                {
                    responses[id.Did] = response.Value;
                }
            }

            List<string> lines = new List<string>();

            // The OSID lives in a different SWMI slot across families (E-series 0xC9, P05c 0xC1), so it
            // is derived from the first candidate that answered rather than fixed to one DID. It is shown
            // on top of the SWMI list because downstream lookups key off it.
            foreach (byte did in Gmlan.OperatingSystemDids)
            {
                if (responses.TryGetValue(did, out byte[] osid))
                {
                    lines.Add("OSID: " + FormatUint32(osid));
                    break;
                }
            }

            foreach (Identifier id in Identifiers)
            {
                if (!responses.TryGetValue(id.Did, out byte[] resp)) continue;
                string value = id.Format == Format.Ascii ? FormatAscii(resp) : FormatUint32(resp);
                if (value == "0") continue;
                lines.Add(id.Name + ": " + value);
            }

            return lines;
        }

        // resp is the full positive response: [0x5A, did, data...].
        private static string FormatAscii(byte[] resp)
        {
            if (resp.Length <= 2) return "";
            string text = Encoding.ASCII.GetString(resp, 2, resp.Length - 2);
            int nul = text.IndexOf('\0');
            if (nul >= 0) text = text.Substring(0, nul);
            return text.Trim();
        }

        // [0x5A, did, b3, b2, b1, b0] -> big-endian uint32, shown as the decimal GM part number.
        private static string FormatUint32(byte[] resp)
        {
            if (resp.Length >= 6)
            {
                uint value = (uint)((resp[2] << 24) | (resp[3] << 16) | (resp[4] << 8) | resp[5]);
                return value.ToString();
            }
            return BitConverter.ToString(resp).Replace("-", " ");
        }
    }
}
