// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;

using PcmHacking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    /// <summary>
    /// Coverage for the shared line store behind the WinForms LogListView and the WPF log panes. The
    /// rules that matter for a bus capture: appended lines keep their order, the cap drops the oldest
    /// (never the newest), and a snapshot always includes the tail that is still queued - the last one
    /// pins the fix for the capture that "stopped" partway and lost lines off the end.
    /// </summary>
    [TestClass]
    public class LogLineBufferTests
    {
        [TestMethod]
        public void DrainMovesPendingIntoLinesInOrder()
        {
            LogLineBuffer buffer = new LogLineBuffer();
            buffer.Append("one");
            buffer.Append("two");
            buffer.Append("three");

            Assert.AreEqual(0, buffer.Count, "Nothing is committed until a drain.");

            LogLineBuffer.DrainResult result = buffer.Drain();

            Assert.AreEqual(3, result.AddedCount);
            Assert.AreEqual(0, result.TrimmedFromStart);
            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, buffer.Lines.ToArray());
        }

        [TestMethod]
        public void DrainWithNoPendingReportsNoChange()
        {
            LogLineBuffer buffer = new LogLineBuffer();

            LogLineBuffer.DrainResult result = buffer.Drain();

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0, result.AddedCount);
        }

        [TestMethod]
        public void MaxLinesDropsOldestKeepsNewest()
        {
            LogLineBuffer buffer = new LogLineBuffer { MaxLines = 3 };
            for (int i = 1; i <= 5; i++)
            {
                buffer.Append("line" + i);
            }

            LogLineBuffer.DrainResult result = buffer.Drain();

            Assert.AreEqual(2, result.TrimmedFromStart, "The two oldest lines are dropped to honour the cap.");
            CollectionAssert.AreEqual(new[] { "line3", "line4", "line5" }, buffer.Lines.ToArray());
        }

        [TestMethod]
        public void MaxLinesZeroIsUnlimited()
        {
            LogLineBuffer buffer = new LogLineBuffer { MaxLines = 0 };
            for (int i = 0; i < 1000; i++)
            {
                buffer.Append("x");
            }

            buffer.Drain();

            Assert.AreEqual(1000, buffer.Count);
        }

        [TestMethod]
        public void SnapshotIncludesUndrainedTail()
        {
            LogLineBuffer buffer = new LogLineBuffer();
            buffer.Append("committed");
            buffer.Drain();

            // The tail arrives after the last drain, exactly as it does when a capture ends between
            // timer ticks. Snapshot must not leave it stranded in the queue.
            buffer.Append("tail1");
            buffer.Append("tail2");

            string snapshot = buffer.Snapshot();

            StringAssert.Contains(snapshot, "committed");
            StringAssert.Contains(snapshot, "tail1");
            StringAssert.Contains(snapshot, "tail2");
            Assert.AreEqual(3, buffer.Count, "Snapshot drains, so the tail is now committed too.");
        }

        [TestMethod]
        public void ClearEmptiesPendingAndCommitted()
        {
            LogLineBuffer buffer = new LogLineBuffer();
            buffer.Append("a");
            buffer.Drain();
            buffer.Append("b");

            buffer.Clear();

            Assert.AreEqual(0, buffer.Count);
            Assert.IsFalse(buffer.HasPending);
            Assert.AreEqual(string.Empty, buffer.Snapshot());
        }
    }
}
