// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// ISO 15765-2 addressing format for one direction of a conversation. Normal addressing puts the
    /// PCI byte first in the CAN frame; extended addressing reserves the first data byte for an
    /// address extension and shifts the PCI (and everything after it) along by one, costing a byte of
    /// payload in every frame.
    /// <para>
    /// The two directions are held separately because GMLAN mixes them: a programming request is
    /// broadcast with extended addressing (id 0x101, extension 0xFE) but each module answers
    /// physically on its own id with normal addressing.
    /// </para>
    /// </summary>
    public readonly struct IsoTpAddressing
    {
        private readonly byte extension;
        private readonly bool extended;

        private IsoTpAddressing(byte extension)
        {
            this.extension = extension;
            this.extended = true;
        }

        /// <summary>Normal addressing: no address extension byte. This is the default value.</summary>
        public static readonly IsoTpAddressing Normal = default;

        /// <summary>Extended addressing with the given address extension as each frame's first byte.</summary>
        public static IsoTpAddressing Extended(byte addressExtension) => new IsoTpAddressing(addressExtension);

        /// <summary>True when frames carry an address extension byte ahead of the PCI.</summary>
        public bool IsExtended => this.extended;

        /// <summary>The address extension byte; meaningful only when <see cref="IsExtended"/>.</summary>
        public byte AddressExtension => this.extension;

        /// <summary>Bytes reserved ahead of the PCI byte: 1 for extended addressing, 0 for normal.</summary>
        public int HeaderLength => this.extended ? 1 : 0;

        public override string ToString() => this.extended ? $"extended(0x{this.extension:X2})" : "normal";
    }
}
