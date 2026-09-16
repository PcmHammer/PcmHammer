// SPDX-License-Identifier: GPL-3.0-only
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The command set that shared, bus-agnostic code needs from a PCM, whichever bus it is reached
    /// on. <see cref="CanCommands"/> implements it over GMLAN and <see cref="Vehicle"/> over VPW.
    /// </summary>
    /// <remarks>
    /// This carries only the operations that are actually consumed through the interface today: the
    /// identifier read (by <see cref="IdentifierSweep"/>) and cleanup (by <see cref="KernelReader"/>
    /// via <see cref="IKernelSession"/>). Both concrete classes expose more shared operations - flash
    /// id, kernel version, reboot, clear codes - but their callers use the concrete types, so those
    /// stay off the interface until the writer merge consumes them polymorphically. The names and
    /// shapes follow the CAN implementation, the more recently factored side.
    /// </remarks>
    public interface IPcmCommands : ISecurityAccess
    {
        /// <summary>
        /// Read one identifier - a VPW 0x3C block or a GMLAN 0x1A DID. Returns the response from the
        /// mode byte onwards: [0x7C, id, data...] or [0x5A, did, data...] for an answer, and
        /// [0x7F, mode, nrc] for a refusal, both as Success. Which of those came back is exactly what
        /// a caller sweeping identifiers is asking.
        /// </summary>
        Task<Response<byte[]>> ReadDataByIdentifier(byte did, CancellationToken cancellationToken);

        /// <summary>
        /// Reboot and then clear codes: the "back to normal" step an operation ends with. Best
        /// effort, so it is safe to await while a cancellation unwinds; never throws.
        /// </summary>
        Task Cleanup(CancellationToken cancellationToken);
    }
}
