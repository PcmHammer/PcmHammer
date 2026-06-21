// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// Addressing for one logical module (the PCM, or later the transmission controller, etc.)
    /// across every bus we can reach it on. A module scan probes a Target on each bus in turn; the
    /// command layer selects a Target to talk to a specific module. The values come from the shared
    /// <see cref="DeviceId"/> (VPW) and <see cref="CanId"/> (CAN) constants, so a module's addresses
    /// are defined in one place rather than hard-coded per device or per message builder.
    /// </summary>
    public sealed class Target
    {
        /// <summary>VPW device id used as the message destination (0x10 PCM, 0x18.. transmission, etc.).</summary>
        public byte VpwId { get; }

        /// <summary>CAN id to transmit to (tool to module). 11-bit OBD2 physical request.</summary>
        public uint CanRequestId { get; }

        /// <summary>CAN id to receive on (module to tool). 11-bit OBD2 physical response.</summary>
        public uint CanResponseId { get; }

        /// <summary>Human-readable name, for logs and scan results.</summary>
        public string Name { get; }

        public Target(string name, byte vpwId, uint canRequestId, uint canResponseId)
        {
            this.Name = name;
            this.VpwId = vpwId;
            this.CanRequestId = canRequestId;
            this.CanResponseId = canResponseId;
        }

        /// <summary>The powertrain/engine control module: VPW 0x10, CAN 0x7E0 / 0x7E8.</summary>
        public static readonly Target Pcm = new Target("PCM", DeviceId.Pcm, CanId.PcmPhysicalRequest, CanId.PcmPhysicalResponse);

        // Adding the next module (e.g. the transmission controller) is one entry here plus a
        // DeviceId constant, for example:
        //   public static readonly Target Transmission =
        //       new Target("TCM", DeviceId.Transmission, CanId.PhysicalRequestBase + 1, CanId.PhysicalResponseBase + 1);
        // No per-device or per-message-builder change is needed for the CAN side; the VPW side
        // becomes retargetable once Protocol's headers are parameterised (planned with that work).
    }
}
