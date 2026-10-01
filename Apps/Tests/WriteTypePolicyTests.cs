// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Unit tests for the two shared facts about a write type: whether it touches flash
    /// (WritePlan.IsDestructive) and what it is allowed to claim on success
    /// (OperationOptions.DescribeCompletion).
    /// </summary>
    /// <remarks>
    /// Both were previously re-derived at each call site. A comparison reported "Write succeeded!" in
    /// the Uno UI because the completion text did not consult the write type at all, and the
    /// destructive predicate existed in four copies that could drift apart independently.
    /// </remarks>
    [TestClass]
    public class WriteTypePolicyTests
    {
        /// <summary>The operations that must never erase or program flash.</summary>
        private static readonly WriteType[] NonDestructive =
        {
            WriteType.Compare,
            WriteType.TestWrite,
        };

        [TestMethod]
        public void Compare_IsNotDestructive()
        {
            Assert.IsFalse(WritePlan.IsDestructive(WriteType.Compare));
        }

        [TestMethod]
        public void TestWrite_IsNotDestructive()
        {
            Assert.IsFalse(WritePlan.IsDestructive(WriteType.TestWrite));
        }

        [TestMethod]
        public void RealWriteTypes_AreDestructive()
        {
            Assert.IsTrue(WritePlan.IsDestructive(WriteType.Full));
            Assert.IsTrue(WritePlan.IsDestructive(WriteType.Calibration));
            Assert.IsTrue(WritePlan.IsDestructive(WriteType.OsPlusCalibrationPlusBoot));
            Assert.IsTrue(WritePlan.IsDestructive(WriteType.Parameters));
        }

        /// <summary>
        /// Every write type other than the two rehearsal modes must be treated as destructive, so a
        /// type added later is caught here rather than silently skipping the boot-sector gate.
        /// </summary>
        [TestMethod]
        public void OnlyCompareAndTestWrite_AreNonDestructive()
        {
            foreach (WriteType writeType in Enum.GetValues(typeof(WriteType)).Cast<WriteType>())
            {
                if (writeType == WriteType.None)
                {
                    continue;
                }

                bool expected = !NonDestructive.Contains(writeType);
                Assert.AreEqual(
                    expected,
                    WritePlan.IsDestructive(writeType),
                    "Unexpected destructive flag for " + writeType);
            }
        }

        /// <summary>
        /// The boot-sector gate must never block a non-destructive operation, whatever the CRCs say -
        /// a comparison of a PCM that cannot rewrite boot is still allowed to run.
        /// </summary>
        [TestMethod]
        public void BootPolicy_AllowsNonDestructiveOperations_EvenWhenBootDiffers()
        {
            List<MemoryRange> ranges = new List<MemoryRange>
            {
                new MemoryRange(0x00000, 0x04000, BlockType.Boot) { ActualCrc = 0xAAAA, DesiredCrc = 0xBBBB },
            };

            foreach (WriteType writeType in NonDestructive)
            {
                Assert.IsTrue(
                    WritePlan.BootPolicyAllowsWritePlan(
                        writeType, supportsBootSectorWrite: false, BlockType.All, 1024 * 1024, ranges),
                    writeType + " must not be blocked by the boot-sector gate.");
            }
        }

        // ---- What an operation is allowed to claim on success ----

        [TestMethod]
        public void Compare_DoesNotClaimAWrite()
        {
            string message = OperationOptions.DescribeCompletion(WriteType.Compare);

            Assert.AreEqual("Comparison completed.", message);
            StringAssert.Contains(message.ToLowerInvariant(), "compar");
        }

        [TestMethod]
        public void TestWrite_DoesNotClaimAWrite()
        {
            Assert.AreEqual("Test completed.", OperationOptions.DescribeCompletion(WriteType.TestWrite));
        }

        [TestMethod]
        public void Write_ClaimsAWrite()
        {
            StringAssert.Contains(
                OperationOptions.DescribeCompletion(WriteType.Full).ToLowerInvariant(),
                "write");
        }

        /// <summary>
        /// The specific regression: an operation that changes nothing must not report success in words
        /// that say it wrote.
        /// </summary>
        [TestMethod]
        public void NonDestructiveOperations_NeverReportAWrite()
        {
            foreach (WriteType writeType in NonDestructive)
            {
                string message = OperationOptions.DescribeCompletion(writeType).ToLowerInvariant();

                Assert.IsFalse(
                    message.Contains("write") || message.Contains("flash"),
                    writeType + " reported \"" + message + "\", which claims the PCM was written.");
            }
        }
    }
}
