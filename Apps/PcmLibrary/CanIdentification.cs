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

        private const byte VinDid = 0x90;

        /// <summary>The display name of the VIN identifier; a caller with its own VIN field filters it out.</summary>
        public const string VinName = "VIN";

        private struct Identifier
        {
            public byte Did;
            public string Name;
            public Format Format;
        }

        // Probed in DID order; the output lists whichever answered, in this order.
        private static readonly Identifier[] Identifiers =
        {
            new Identifier { Did = VinDid, Name = VinName,                Format = Format.Ascii },
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

        /// <summary>
        /// One identifier the module reported, ready to display: its GMLAN name and the formatted
        /// value. A SWMI item's value is the part number of one flashed software or calibration
        /// segment.
        /// </summary>
        public class Item
        {
            public string Name { get; }
            public string Value { get; }

            public Item(string name, string value)
            {
                this.Name = name;
                this.Value = value;
            }

            public override string ToString()
            {
                return this.Name + ": " + this.Value;
            }
        }

        /// <summary>
        /// The identification a CAN PCM reported: the values a caller can show in its own fields,
        /// every identifier that answered, and the whole list already formatted for a log.
        /// </summary>
        public class Identity
        {
            /// <summary>Operating system id, or zero if no candidate DID answered with a usable one.</summary>
            public uint Osid { get; }

            /// <summary>VIN, or empty if the module did not report one.</summary>
            public string Vin { get; }

            /// <summary>Every identifier that answered, in DID order.</summary>
            public IReadOnlyList<Item> Items { get; }

            /// <summary>The OSID and every identifier that answered, formatted "Name: value".</summary>
            public IReadOnlyList<string> Lines { get; }

            public Identity(uint osid, string vin, IReadOnlyList<Item> items, IReadOnlyList<string> lines)
            {
                this.Osid = osid;
                this.Vin = vin;
                this.Items = items;
                this.Lines = lines;
            }
        }

        /// <summary>
        /// Read every supported identifier and return both the values and their formatted form, so a
        /// UI with its own fields and a UI that just logs the list share one implementation.
        /// </summary>
        public static async Task<Identity> Query(CanCommands commands, CancellationToken cancellationToken)
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

            // The OSID lives in a different SWMI slot across families, so it is derived from the first
            // candidate that answered with a usable value rather than fixed to one DID. A module that
            // answers but leaves the slot empty (all zeroes or all ones) falls through to the next.
            // Shown on top of the SWMI list because downstream lookups key off it.
            uint operatingSystemId = 0;
            foreach (byte did in Gmlan.OperatingSystemDids)
            {
                if (responses.TryGetValue(did, out byte[] osid)
                    && TryGetUint32(osid, out uint osidValue)
                    && Gmlan.IsUsableOsid(osidValue))
                {
                    operatingSystemId = osidValue;
                    lines.Add("OSID: " + osidValue);
                    break;
                }
            }

            string vin = responses.TryGetValue(VinDid, out byte[] vinResponse)
                ? FormatAscii(vinResponse)
                : string.Empty;

            List<Item> items = new List<Item>();
            foreach (Identifier id in Identifiers)
            {
                if (!responses.TryGetValue(id.Did, out byte[] resp)) continue;
                string value = id.Format == Format.Ascii ? FormatAscii(resp) : FormatUint32(resp);
                if (value == "0") continue;
                Item item = new Item(id.Name, value);
                items.Add(item);
                lines.Add(item.ToString());
            }

            return new Identity(operatingSystemId, vin, items, lines);
        }

        /// <summary>
        /// Read the identification and return just the formatted lines.
        /// </summary>
        public static async Task<List<string>> Read(CanCommands commands, CancellationToken cancellationToken)
        {
            Identity identity = await Query(commands, cancellationToken);
            return new List<string>(identity.Lines);
        }

        /// <summary>The names this module's known DIDs are displayed under, for a sweep.</summary>
        public static IReadOnlyDictionary<byte, string> IdentifierNames { get; } =
            Identifiers.ToDictionary(i => i.Did, i => i.Name);

        // resp is the full positive response: [0x5A, did, data...].
        private static string FormatAscii(byte[] resp)
        {
            if (resp.Length <= 2) return "";
            string text = Encoding.ASCII.GetString(resp, 2, resp.Length - 2);
            int nul = text.IndexOf('\0');
            if (nul >= 0) text = text.Substring(0, nul);
            return text.Trim();
        }

        // [0x5A, did, b3, b2, b1, b0] -> big-endian uint32. False when the response is too short.
        private static bool TryGetUint32(byte[] resp, out uint value)
        {
            if (resp.Length >= 6)
            {
                value = (uint)((resp[2] << 24) | (resp[3] << 16) | (resp[4] << 8) | resp[5]);
                return true;
            }

            value = 0;
            return false;
        }

        // [0x5A, did, b3, b2, b1, b0] -> big-endian uint32, shown as the decimal GM part number.
        private static string FormatUint32(byte[] resp)
        {
            return TryGetUint32(resp, out uint value)
                ? value.ToString()
                : BitConverter.ToString(resp).Replace("-", " ");
        }
    }
}
