// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// A device that can be pointed at a CAN target's request/response ids. Implemented by every
    /// CAN-capable device (AVT, OBDX, J2534, ...) whether it uses software or native ISO-TP, so the
    /// command layer can select a Target without knowing the concrete device type.
    /// </summary>
    public interface ICanTarget
    {
        /// <summary>CAN id to transmit to (tool to module).</summary>
        uint TxCanId { get; set; }

        /// <summary>CAN id to accept (module to tool).</summary>
        uint RxCanId { get; set; }
    }
}
