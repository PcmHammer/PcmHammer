// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// The source names in use, and which of them each connection can supply.
    /// </summary>
    public static class ParameterSources
    {
        /// <summary>Asked for by PID or address, over either bus.</summary>
        public const string GmEnhancedObd = "GM Enhanced OBD";

        /// <summary>Computed from other values in the same row.</summary>
        public const string Math = "Math";

        /// <summary>Transmitted by other modules whether anyone asks or not.</summary>
        public const string Broadcast = "Broadcast";

        /// <summary>What the interface talking to the PCM can supply.</summary>
        public static readonly IReadOnlyList<string> PcmConnection = new[] { GmEnhancedObd, Math };

        /// <summary>What the interface watching a second bus can supply.</summary>
        public static readonly IReadOnlyList<string> AuxiliaryConnection = new[] { Broadcast };
    }
}
