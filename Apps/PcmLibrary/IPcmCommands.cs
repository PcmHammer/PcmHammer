// SPDX-License-Identifier: GPL-3.0-only
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The command set every PCM offers, whichever bus it is reached on. <see cref="CanCommands"/>
    /// implements it over GMLAN and <see cref="Vehicle"/> over VPW, so code that needs only these
    /// operations can be written once instead of once per bus.
    /// </summary>
    /// <remarks>
    /// The names and shapes are the CAN implementation's, which is the more recently factored of the
    /// two, so that side implements this without changing. Flash and memory operations are
    /// deliberately absent: their VPW and CAN forms still differ in more than naming, and they belong
    /// here only once the readers and writers that drive them are merged.
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

        /// <summary>Ask the running kernel for the flash chip's manufacturer and device id.</summary>
        Task<Response<uint>> GetFlashId(CancellationToken cancellationToken);

        /// <summary>
        /// Ask the running kernel for its version, packed as (epoch &lt;&lt; 8) | pcmType. An error
        /// means no kernel answered, which is how callers detect that none is running.
        /// </summary>
        Task<Response<ulong>> GetKernelVersion(CancellationToken cancellationToken);

        /// <summary>Return the PCM to its stock operating system, without clearing codes.</summary>
        Task<bool> Reboot(CancellationToken cancellationToken, bool announce = true);

        /// <summary>Clear the trouble codes a programming session provokes across the bus.</summary>
        Task ClearDiagnosticCodes(CancellationToken cancellationToken, bool announce = true);

        /// <summary>
        /// Reboot and then clear codes: the "back to normal" step an operation ends with. Best
        /// effort, so it is safe to await while a cancellation unwinds; never throws.
        /// </summary>
        Task Cleanup(CancellationToken cancellationToken);
    }
}
