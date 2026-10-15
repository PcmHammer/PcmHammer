// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// The device-independent filter sets, and what an interface that can only filter on the
    /// destination byte is able to make of them.
    /// </summary>
    [TestClass]
    public class BusFilterTests
    {
        [TestMethod]
        public void VPW_AcceptsPcmRepliesAndRejectsTheRest()
        {
            BusFilter filter = BusFilters.VPW[0];

            Assert.IsTrue(filter.Accepts(new byte[] { 0x6C, 0xF0, 0x10, 0x59, 0x00, 0x00, 0xFF }));

            // A functional reply goes to the emissions address, not to the tool. Letting this
            // through is the whole reason the diagnostics set exists.
            Assert.IsFalse(filter.Accepts(new byte[] { 0x48, 0x6B, 0x10, 0x47, 0x00 }));
        }

        [TestMethod]
        public void VPWDiagnostics_AcceptsBothRepliesBetweenThem()
        {
            bool toolHeard = false;
            bool emissionsHeard = false;

            foreach (BusFilter filter in BusFilters.VPWDiagnostics)
            {
                if (filter.Accepts(new byte[] { 0x6C, 0xF0, 0x10, 0x7F, 0x03, 0x11 }))
                {
                    toolHeard = true;
                }

                if (filter.Accepts(new byte[] { 0x48, 0x6B, 0x10, 0x47, 0x00 }))
                {
                    emissionsHeard = true;
                }
            }

            // The refusal comes back physically and the list functionally; both are needed.
            Assert.IsTrue(toolHeard);
            Assert.IsTrue(emissionsHeard);
        }

        [TestMethod]
        public void Destinations_AreReadableForDestinationOnlyInterfaces()
        {
            Assert.IsTrue(BusFilters.TryGetDestinations(
                BusFilters.VPW, out IReadOnlyList<byte> narrow));
            CollectionAssert.AreEqual(new byte[] { 0xF0 }, (System.Collections.ICollection)narrow);

            Assert.IsTrue(BusFilters.TryGetDestinations(
                BusFilters.VPWDiagnostics, out IReadOnlyList<byte> wide));
            CollectionAssert.AreEqual(new byte[] { 0xF0, 0x6B }, (System.Collections.ICollection)wide);
        }

        [TestMethod]
        public void Destinations_AreNotReadableWhenNothingIsPinned()
        {
            // "Everything" pins no destination, so an interface that filters only on that byte
            // cannot install this set and has to say so rather than guess at a value.
            Assert.IsFalse(BusFilters.TryGetDestinations(
                BusFilters.All, out IReadOnlyList<byte> _));

            Assert.IsFalse(BusFilters.TryGetDestinations(
                new BusFilter[0], out IReadOnlyList<byte> _));
        }

        [TestMethod]
        public void ShortMessages_AreNotJudged()
        {
            // Too short to carry a header, so there is nothing to compare; dropping these would
            // hide device-level replies that are not bus traffic at all.
            Assert.IsTrue(BusFilters.VPW[0].Accepts(new byte[] { 0x6C, 0xF0 }));
            Assert.IsTrue(BusFilters.VPW[0].Accepts(null));
        }
    }
}
