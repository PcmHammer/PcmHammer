// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>Which list a code came from, which is what says how much to worry about it.</summary>
    public enum DiagnosticCodeKind
    {
        /// <summary>Confirmed, and the light is on. Mode 03.</summary>
        Stored,

        /// <summary>Seen once, not yet confirmed. Mode 07.</summary>
        Pending,

        /// <summary>Confirmed and not clearable by hand; the module clears it. Mode 0A.</summary>
        Permanent,

        /// <summary>Reported by the module's own service, with a status byte. Mode 19.</summary>
        Reported,
    }

    /// <summary>
    /// One trouble code: the two bytes a module reports, and what they spell.
    /// </summary>
    /// <remarks>
    /// The encoding is fixed: the top two bits choose the letter, the next two are the first digit,
    /// and the remaining twelve bits are three hex digits. That is why a code can contain A-F -
    /// P1A00 is not a typo, it is the low twelve bits of 0x1A00 written out.
    /// </remarks>
    public sealed class DiagnosticCode
    {
        private static readonly char[] SystemLetters = { 'P', 'C', 'B', 'U' };

        public DiagnosticCode(ushort raw, DiagnosticCodeKind kind, string? description, byte? status = null)
        {
            this.Raw = raw;
            this.Kind = kind;
            this.Description = description;
            this.Status = status;
            this.Name = Decode(raw);
        }

        /// <summary>
        /// The module's own status byte, when it came from a service that reports one. Null for the
        /// generic services, which say only which list a code is in.
        /// </summary>
        public byte? Status { get; }

        /// <summary>The two bytes as reported.</summary>
        public ushort Raw { get; }

        /// <summary>"P0143", "U2100", and so on.</summary>
        public string Name { get; }

        public DiagnosticCodeKind Kind { get; }

        /// <summary>What it means, or null when no definition is known for it.</summary>
        public string? Description { get; }

        /// <summary>
        /// Turn the two reported bytes into the code people quote.
        /// </summary>
        public static string Decode(ushort raw)
        {
            char letter = SystemLetters[(raw >> 14) & 0x03];
            int firstDigit = (raw >> 12) & 0x03;

            return letter + firstDigit.ToString() + (raw & 0x0FFF).ToString("X3");
        }

        public override string ToString() =>
            this.Description == null ? this.Name : this.Name + " - " + this.Description;
    }
}
