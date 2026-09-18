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

        /// <summary>
        /// ISO-TP addressing for transmitted frames. Set to <see cref="IsoTpAddressing.Extended"/>
        /// to address a functional/extended conversation such as the GMLAN all-nodes broadcast;
        /// only meaningful when <see cref="SupportsExtendedAddressing"/> is true.
        /// </summary>
        IsoTpAddressing TxAddressing { get; set; }

        /// <summary>
        /// ISO-TP addressing for received frames. Held separately from <see cref="TxAddressing"/>:
        /// a GMLAN extended-addressed broadcast is answered physically with normal addressing.
        /// </summary>
        IsoTpAddressing RxAddressing { get; set; }

        /// <summary>
        /// Whether this device can frame extended-addressed ISO-TP. False for interfaces whose
        /// firmware owns the framing and exposes no way to set the address extension, so the
        /// command layer can decline rather than transmit a frame the module will not understand.
        /// </summary>
        bool SupportsExtendedAddressing { get; }
    }
}
