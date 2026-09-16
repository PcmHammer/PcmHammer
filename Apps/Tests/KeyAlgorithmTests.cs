// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace Tests
{
    [TestClass]
    public class KeyAlgorithmTests
    {
        /// <summary>
        /// The older E92/E92a (2-byte security) key algorithm (GM_OTHER #1), captured from a bench
        /// unlock: seed 0x9D19 -> key 0x04EB (OSID 12672612).
        /// </summary>
        [TestMethod]
        public void E92LegacyCanAlgorithm_MatchesTheBenchCapture()
        {
            Assert.AreEqual(
                (ushort)0x04EB,
                KeyAlgorithm.GetCanKey(KeyAlgorithm.E92LegacyCanAlgorithm, 0x9D19));
        }

        /// <summary>The older E92 unlocks with that algorithm on its 16-bit (2-byte seed) path.</summary>
        [TestMethod]
        public void E92_UsesTheLegacyCanAlgorithm()
        {
            Assert.AreEqual(KeyAlgorithm.E92LegacyCanAlgorithm, new OSIDInfo(PcmType.E92).KeyAlgorithm);
        }
    }
}
