// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// Builders for inbound message filters (see <see cref="Device.FilterInbound"/>).
    /// Kept in one place so every exchange that wants to ignore off-conversation bus
    /// traffic uses the same addressing rule rather than re-deriving it.
    /// </summary>
    public static class MessageFilters
    {
        /// <summary>
        /// Build a filter that keeps only the replies to a given outgoing request: a VPW
        /// message is [priority][dest][src][mode]..., so a genuine reply to a physical
        /// request is addressed to the tool (dest == Tool) and comes from the module we
        /// sent to (src == the request's dest). Everything else on the bus is dropped.
        /// </summary>
        /// <remarks>
        /// Conservative by design: messages too short to carry addressing are accepted and
        /// left to the response selector, so this never drops anything the legacy code would
        /// have processed. Broadcast / functional requests (dest in the tool/broadcast range,
        /// 0xF0-0xFF) are not narrowed by source, since replies legitimately come from many
        /// modules.
        /// </remarks>
        public static Predicate<Message> RepliesFrom(Message request)
        {
            if (request == null || request.Length < 3)
            {
                return _ => true;
            }

            byte target = request[1];
            bool broadcast = target >= 0xF0; // DeviceId.Tool (0xF0) .. Broadcast (0xFE)

            return received =>
            {
                if (received == null || received.Length < 3)
                {
                    return true; // can't classify; preserve legacy behavior
                }

                if (received[1] != DeviceId.Tool)
                {
                    return false; // addressed to some other module — not our conversation
                }

                return broadcast || received[2] == target;
            };
        }
    }
}
