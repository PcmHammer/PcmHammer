// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// Well-known CAN identifiers for OBD2 and GMLAN diagnostics. These are the standard target
    /// addresses; a device's transmit/receive IDs are set from here by default but are settable, so
    /// the command layer can address a different module (or a 29-bit ID) when needed.
    /// </summary>
    public static class CanId
    {
        // ── OBD2 standard 11-bit ─────────────────────────────────────────────────

        /// <summary>Functional (broadcast) request to all ECUs.</summary>
        public const uint OBD2Functional = 0x7DF;

        /// <summary>Physical request to the PCM (tool to PCM), 11-bit.</summary>
        public const uint PcmPhysicalRequest = 0x7E0;

        /// <summary>Physical response from the PCM (PCM to tool), 11-bit.</summary>
        public const uint PcmPhysicalResponse = 0x7E8;

        /// <summary>Base for 11-bit physical request IDs; add ECU index 0-7 (ECU 0 = 0x7E0).</summary>
        public const uint PhysicalRequestBase = 0x7E0;

        /// <summary>Base for 11-bit physical response IDs; add ECU index 0-7 (ECU 0 = 0x7E8).</summary>
        public const uint PhysicalResponseBase = 0x7E8;

        // ── GMLAN 29-bit extended ────────────────────────────────────────────────

        /// <summary>GMLAN functional (broadcast) request, 29-bit extended.</summary>
        public const uint GmlanFunctional = 0x18DB33F1;

        /// <summary>GMLAN physical request to the PCM (tool 0xF1 to PCM 0x00), 29-bit extended.</summary>
        public const uint GmlanPcmRequest = 0x18DA00F1;

        /// <summary>GMLAN physical response from the PCM (PCM 0x00 to tool 0xF1), 29-bit extended.</summary>
        public const uint GmlanPcmResponse = 0x18DAF100;
    }
}
