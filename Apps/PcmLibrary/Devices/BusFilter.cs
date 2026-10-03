// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace PcmHacking
{
    /// <summary>
    /// One conversation the interface should deliver, as a mask and pattern over a message's
    /// leading bytes.
    /// </summary>
    /// <remarks>
    /// Device-independent on purpose. An interface that filters in hardware installs these; one that
    /// cannot keeps everything and lets the software filter sort it out. Either way the application
    /// asks for the same thing, so a conversation that works on one interface works on all of them.
    /// </remarks>
    public sealed class BusFilter
    {
        public BusFilter(uint mask, uint pattern, string description)
        {
            this.Mask = mask;
            this.Pattern = pattern;
            this.Description = description;
        }

        /// <summary>Which bits of the leading bytes are compared.</summary>
        public uint Mask { get; }

        /// <summary>What those bits must equal.</summary>
        public uint Pattern { get; }

        /// <summary>For the debug log, so an installed filter can be recognised.</summary>
        public string Description { get; }

        /// <summary>
        /// The destination byte this filter pins down, for the several interfaces that can only
        /// filter on that one byte rather than on a mask and pattern.
        /// </summary>
        public bool TryGetDestination(out byte destination)
        {
            destination = 0;

            if ((this.Mask & 0x00FF00) != 0x00FF00)
            {
                return false;
            }

            destination = (byte)((this.Pattern >> 8) & 0xFF);
            return true;
        }

        public bool Accepts(byte[] message)
        {
            if (message == null || message.Length < 3)
            {
                return true;
            }

            uint leading = (uint)((message[0] << 16) | (message[1] << 8) | message[2]);
            return (leading & this.Mask) == (this.Pattern & this.Mask);
        }

        public override string ToString() =>
            $"{this.Description} (mask {this.Mask:X6}, pattern {this.Pattern:X6})";
    }

    /// <summary>
    /// The filters a bus needs for the application to hear what it asks for.
    /// </summary>
    public static class BusFilters
    {
        /// <summary>
        /// VPW, as used for everything the application normally does: replies from the PCM to the
        /// tool. Deliberately narrow - a VPW bus carries a lot of traffic nobody here asked for, and
        /// every message that reaches the application is one the logging loop has to look at.
        /// </summary>
        public static readonly IReadOnlyList<BusFilter> VPW = new[]
        {
            new BusFilter(0xFEFFFF, 0x6CF010, "PCM replies to the tool"),
        };

        /// <summary>
        /// VPW while asking the generic OBD-II questions.
        /// </summary>
        /// <remarks>
        /// A reply to a physically addressed request comes back to the tool; a reply to a
        /// functionally addressed one does not - it goes to the functional address instead. Without
        /// the second filter those answers never reach the application, which looks exactly like a
        /// module that did not respond.
        /// </remarks>
        public static readonly IReadOnlyList<BusFilter> VPWDiagnostics = new[]
        {
            new BusFilter(0xFEFFFF, 0x6CF010, "PCM replies to the tool"),
            new BusFilter(0x00FF00, 0x006B00, "emissions replies"),
        };

        /// <summary>Everything, for a bus being watched rather than talked to.</summary>
        public static readonly IReadOnlyList<BusFilter> All = new[]
        {
            new BusFilter(0x000000, 0x000000, "everything"),
        };

        /// <summary>
        /// The destination bytes a set of filters asks for, for an interface that filters on that
        /// byte alone. False when any filter in the set does not pin one, which means the interface
        /// has to pass everything and let the software sort it out.
        /// </summary>
        public static bool TryGetDestinations(
            IReadOnlyList<BusFilter> filters, out IReadOnlyList<byte> destinations)
        {
            List<byte> found = new List<byte>();
            destinations = found;

            if (filters == null || filters.Count == 0)
            {
                return false;
            }

            foreach (BusFilter filter in filters)
            {
                if (!filter.TryGetDestination(out byte destination))
                {
                    return false;
                }

                if (!found.Contains(destination))
                {
                    found.Add(destination);
                }
            }

            return true;
        }
    }
}
