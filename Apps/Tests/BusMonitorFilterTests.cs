// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;

using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Coverage for the CAN id filter shared by every front end (BusMonitor.ParseCanIds and the
    /// DefaultCanIds it starts from). The parse used to live in the WinForms bus monitor tab, which
    /// meant the WPF tab and the CLI each needed their own copy; these tests pin the behaviour now
    /// that all three call the same one. The key rule is that a bad filter widens the view rather
    /// than emptying it - a typo must never leave the user staring at a silent monitor.
    /// </summary>
    [TestClass]
    public class BusMonitorFilterTests
    {
        private static uint[] Parse(string text)
        {
            IReadOnlyCollection<uint>? ids = BusMonitor.ParseCanIds(text);
            return ids == null ? null! : ids.ToArray();
        }

        [TestMethod]
        public void SpaceSeparatedHexIdsAreParsed()
        {
            CollectionAssert.AreEqual(new uint[] { 0x7E0, 0x7E8, 0x101 }, Parse("7E0 7E8 101"));
        }

        [TestMethod]
        public void IdsAreHexNotDecimal()
        {
            // "101" is 0x101, not one hundred and one: the ids the user types are the ids on the bus.
            CollectionAssert.AreEqual(new uint[] { 0x101 }, Parse("101"));
        }

        [TestMethod]
        public void LowerCaseHexIsAccepted()
        {
            CollectionAssert.AreEqual(new uint[] { 0x7e0 }, Parse("7e0"));
        }

        [TestMethod]
        public void CommasAndTabsSeparateIdsToo()
        {
            CollectionAssert.AreEqual(new uint[] { 0x7E0, 0x7E8, 0x101 }, Parse("7E0,7E8\t101"));
        }

        [TestMethod]
        public void ExtraWhitespaceIsIgnored()
        {
            CollectionAssert.AreEqual(new uint[] { 0x7E0, 0x7E8 }, Parse("   7E0    7E8   "));
        }

        [TestMethod]
        public void UnparseableTokensAreSkippedRatherThanFailingTheWholeFilter()
        {
            // One fat-fingered id shouldn't cost the user the ids they got right.
            CollectionAssert.AreEqual(new uint[] { 0x7E0, 0x101 }, Parse("7E0 zzz 101"));
        }

        [TestMethod]
        public void EmptyFilterMeansAcceptEveryId()
        {
            Assert.IsNull(BusMonitor.ParseCanIds(string.Empty));
            Assert.IsNull(BusMonitor.ParseCanIds("   "));
            Assert.IsNull(BusMonitor.ParseCanIds(null));
        }

        [TestMethod]
        public void EntirelyUnparseableFilterMeansAcceptEveryId()
        {
            // Showing everything is recoverable; showing nothing looks like broken hardware.
            Assert.IsNull(BusMonitor.ParseCanIds("nonsense"));
        }

        [TestMethod]
        public void DefaultFilterTextRoundTripsToTheDefaultIds()
        {
            // The filter box is pre-filled with DefaultCanFilter, so parsing it back has to give
            // exactly DefaultCanIds - otherwise the box would lie about what is being captured.
            CollectionAssert.AreEqual(BusMonitor.DefaultCanIds.ToArray(), Parse(BusMonitor.DefaultCanFilter));
        }

        [TestMethod]
        public void DefaultIdsAreTheToolPcmPairAndTheAllNodeRequest()
        {
            CollectionAssert.AreEqual(new uint[] { 0x7E0, 0x7E8, 0x101 }, BusMonitor.DefaultCanIds.ToArray());
        }

        [TestMethod]
        public void FormatCanIdsProducesThreeDigitUpperCaseHex()
        {
            Assert.AreEqual("7E0 7E8 101", BusMonitor.FormatCanIds(new uint[] { 0x7E0, 0x7E8, 0x101 }));
        }
    }
}
