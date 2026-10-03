// SPDX-License-Identifier: GPL-3.0-only
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PcmHacking;

namespace PcmHacking.Tests
{
    /// <summary>
    /// The hand-off that lets the logger outrun the display.
    /// </summary>
    [TestClass]
    public class LatestValueFeedTests
    {
        [TestMethod]
        public void NothingPublishedMeansNothingToDraw()
        {
            LatestValueFeed<string> feed = new LatestValueFeed<string>();

            Assert.IsNull(feed.TakeLatest(), "null is the signal to skip the repaint");
            Assert.AreEqual(0, feed.Produced);
        }

        [TestMethod]
        public void TakingRemovesTheItem()
        {
            LatestValueFeed<string> feed = new LatestValueFeed<string>();
            feed.Publish("one");

            Assert.AreEqual("one", feed.TakeLatest());
            Assert.IsNull(feed.TakeLatest(), "the same frame is not drawn twice");
        }

        /// <summary>
        /// The whole point: a producer running faster than the consumer overwrites, and the consumer
        /// sees only the most recent value rather than falling behind through a backlog.
        /// </summary>
        [TestMethod]
        public void FasterProducerOverwritesRatherThanQueues()
        {
            LatestValueFeed<string> feed = new LatestValueFeed<string>();

            feed.Publish("first");
            feed.Publish("second");
            feed.Publish("third");

            Assert.AreEqual("third", feed.TakeLatest());
            Assert.IsNull(feed.TakeLatest());
        }

        [TestMethod]
        public void CountersSeparateSlowDisplayFromSlowLogging()
        {
            LatestValueFeed<string> feed = new LatestValueFeed<string>();

            feed.Publish("a");
            feed.Publish("b");
            feed.Publish("c");
            feed.TakeLatest();

            Assert.AreEqual(3, feed.Produced);
            Assert.AreEqual(1, feed.Delivered);
            Assert.AreEqual(2, feed.Dropped, "two were overwritten before being drawn");
        }

        /// <summary>An item still waiting has not been dropped, and must not be counted as such.</summary>
        [TestMethod]
        public void PendingItemIsNotCountedAsDropped()
        {
            LatestValueFeed<string> feed = new LatestValueFeed<string>();

            feed.Publish("a");

            Assert.AreEqual(0, feed.Dropped);
        }

        [TestMethod]
        public void ResetClearsEverything()
        {
            LatestValueFeed<string> feed = new LatestValueFeed<string>();
            feed.Publish("a");
            feed.TakeLatest();
            feed.Publish("b");

            feed.Reset();

            Assert.IsNull(feed.TakeLatest());
            Assert.AreEqual(0, feed.Produced);
            Assert.AreEqual(0, feed.Delivered);
        }

        [TestMethod]
        public void NullIsRejected()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new LatestValueFeed<string>().Publish(null!));
        }
    }
}
