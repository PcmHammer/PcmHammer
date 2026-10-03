// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// The sample buffer behind the monitors: scrolling, cursor placement and wrapping.
    /// </summary>
    [TestClass]
    public class LogHistoryTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static LogHistory Create()
        {
            return new LogHistory(new[] { "rpm", "map", "tps" });
        }

        /// <summary>Fill with one sample a second, value = sample number plus a per-column offset.</summary>
        private static void Fill(LogHistory history, int sampleCount)
        {
            for (int index = 0; index < sampleCount; index++)
            {
                history.Append(Start.AddSeconds(index), new double[] { index, index + 100, index + 200 });
            }
        }

        [TestMethod]
        public void StartsEmpty()
        {
            LogHistory history = Create();

            Assert.AreEqual(0, history.Count);
            Assert.AreEqual(3, history.ColumnCount);
            Assert.AreEqual(TimeSpan.Zero, history.Duration);
        }

        [TestMethod]
        public void SamplesReadBackInOrder()
        {
            LogHistory history = Create();
            Fill(history, 4);

            Assert.AreEqual(4, history.Count);
            Assert.AreEqual(Start, history.GetTimestamp(0));
            Assert.AreEqual(Start.AddSeconds(3), history.GetTimestamp(3));
            Assert.AreEqual(0.0, history.GetValue(0, 0));
            Assert.AreEqual(103.0, history.GetValue(3, 1));
            Assert.AreEqual(TimeSpan.FromSeconds(3), history.Duration);
        }

        /// <summary>
        /// Nothing is ever dropped, so an index taken early in a session still refers to the same
        /// sample much later - a monitor cursor never has to be re-based.
        /// </summary>
        [TestMethod]
        public void NothingIsDroppedAsTheSessionGrows()
        {
            LogHistory history = Create();
            Fill(history, 3000);

            Assert.AreEqual(3000, history.Count);
            Assert.AreEqual(Start, history.GetTimestamp(0), "the first sample is still the first");
            Assert.AreEqual(0.0, history.GetValue(0, 0));
            Assert.AreEqual(2999.0, history.GetValue(2999, 0));
            Assert.AreEqual(TimeSpan.FromSeconds(2999), history.Duration);
        }

        /// <summary>Storage is chunked, so the seams between chunks are worth exercising.</summary>
        [TestMethod]
        public void ValuesStayAlignedAcrossChunkBoundaries()
        {
            LogHistory history = Create();
            Fill(history, 2500);

            foreach (int index in new[] { 0, 1023, 1024, 1025, 2047, 2048, 2499 })
            {
                double rpm = history.GetValue(index, 0);
                Assert.AreEqual(index, rpm, $"rpm at {index}");
                Assert.AreEqual(rpm + 100, history.GetValue(index, 1), $"map at {index}");
                Assert.AreEqual(rpm + 200, history.GetValue(index, 2), $"tps at {index}");
                Assert.AreEqual(Start.AddSeconds(index), history.GetTimestamp(index), $"time at {index}");
            }
        }

        [TestMethod]
        public void IndexAtOrBeforeFindsTheCursorSample()
        {
            LogHistory history = Create();
            Fill(history, 5);

            Assert.AreEqual(0, history.IndexAtOrBefore(Start));
            Assert.AreEqual(2, history.IndexAtOrBefore(Start.AddSeconds(2)));
            Assert.AreEqual(2, history.IndexAtOrBefore(Start.AddSeconds(2.9)), "rounds back to the sample");
            Assert.AreEqual(4, history.IndexAtOrBefore(Start.AddSeconds(99)), "clamps to the newest");
        }

        [TestMethod]
        public void IndexAtOrBeforeRejectsTimesBeforeTheHistory()
        {
            LogHistory history = Create();
            Fill(history, 3);

            Assert.AreEqual(-1, history.IndexAtOrBefore(Start.AddSeconds(-1)));
            Assert.AreEqual(-1, Create().IndexAtOrBefore(Start), "empty history");
        }

        /// <summary>
        /// A row that omits a parameter must not shift the others along; the gap is NaN so the chart
        /// can show a break rather than a wrong value.
        /// </summary>
        [TestMethod]
        public void LoggerRowMatchesByParameterIdAndGapsBecomeNaN()
        {
            LogHistory history = Create();

            history.Append(Start, new[]
            {
                new LogRowElement("tps", "Throttle", "%", "12.5", 12.5),
                new LogRowElement("rpm", "Engine Speed", "RPM", "850", 850),
            });

            Assert.AreEqual(850.0, history.GetValue(0, 0), "rpm went to its own column, not the first");
            Assert.IsTrue(double.IsNaN(history.GetValue(0, 1)), "map was not reported");
            Assert.AreEqual(12.5, history.GetValue(0, 2));
        }

        [TestMethod]
        public void UnknownParameterInARowIsIgnored()
        {
            LogHistory history = Create();

            history.Append(Start, new[] { new LogRowElement("not-in-profile", "Other", "", "1", 1) });

            Assert.AreEqual(1, history.Count);
            Assert.IsTrue(double.IsNaN(history.GetValue(0, 0)));
        }

        [TestMethod]
        public void ClearEmptiesWithoutDisturbingTheSchema()
        {
            LogHistory history = Create();
            Fill(history, 5);

            history.Clear();

            Assert.AreEqual(0, history.Count);
            Assert.AreEqual(3, history.ColumnCount);

            Fill(history, 2);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(Start, history.GetTimestamp(0), "reuse starts clean");
        }

        [TestMethod]
        public void ColumnLookupIsByIdAndCaseInsensitive()
        {
            LogHistory history = Create();

            Assert.AreEqual(0, history.IndexOfColumn("rpm"));
            Assert.AreEqual(1, history.IndexOfColumn("MAP"));
            Assert.AreEqual(-1, history.IndexOfColumn("missing"));
        }

        [TestMethod]
        public void OutOfRangeSampleIndexThrows()
        {
            LogHistory history = Create();
            Fill(history, 2);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => history.GetTimestamp(2));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => history.GetTimestamp(-1));
        }

        [TestMethod]
        public void WrongSizedSampleIsRejected()
        {
            LogHistory history = Create();

            Assert.ThrowsException<ArgumentException>(() => history.Append(Start, new double[] { 1, 2 }));
        }

        [TestMethod]
        public void BinarySearchWorksAcrossChunks()
        {
            LogHistory history = Create();
            Fill(history, 2500);

            Assert.AreEqual(0, history.IndexAtOrBefore(Start));
            Assert.AreEqual(1024, history.IndexAtOrBefore(Start.AddSeconds(1024)));
            Assert.AreEqual(2499, history.IndexAtOrBefore(Start.AddSeconds(99999)));
        }
    }
}
