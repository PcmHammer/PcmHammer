// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;

namespace PcmHacking
{
    /// <summary>
    /// A one-slot hand-off from a producer that must not be slowed down to a consumer that redraws
    /// at its own pace. The slot holds only the most recent item; anything the consumer did not
    /// collect in time is dropped.
    /// </summary>
    /// <remarks>
    /// This is what keeps the display off the logger's back. Logging runs as fast as the bus and the
    /// PCM allow, and every sample still reaches the log file and the history; the screen is a
    /// different matter, because a repaint costs far more than a sample and a UI that tries to draw
    /// every one will fall behind and stay behind. Marshalling each sample to the UI thread is the
    /// same trap in a different form: the message queue grows without bound and the window stops
    /// responding while the backlog drains.
    ///
    /// So the producer overwrites, the consumer takes what is there when it looks, and a dropped
    /// frame costs nothing - the next one is more current anyway. <see cref="Dropped"/> makes that
    /// visible rather than silent, so "the display is behind" can be told from "logging is slow".
    /// </remarks>
    public sealed class LatestValueFeed<T>
        where T : class
    {
        private T? pending;
        private long produced;
        private long delivered;

        /// <summary>How many items the producer has published.</summary>
        public long Produced => Interlocked.Read(ref this.produced);

        /// <summary>How many the consumer actually collected.</summary>
        public long Delivered => Interlocked.Read(ref this.delivered);

        /// <summary>
        /// How many were overwritten before the consumer saw them. Expected to be non-zero whenever
        /// the producer outruns the refresh rate, which is the intended behaviour, not a fault.
        /// </summary>
        public long Dropped => this.Produced - this.Delivered - (Volatile.Read(ref this.pending) == null ? 0 : 1);

        /// <summary>Publish the latest item, discarding any the consumer has not taken.</summary>
        public void Publish(T value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Interlocked.Increment(ref this.produced);
            Volatile.Write(ref this.pending, value);
        }

        /// <summary>
        /// Take the most recent item, or null if nothing new has arrived. Returning null is the
        /// signal to skip the repaint entirely rather than redraw identical content.
        /// </summary>
        public T? TakeLatest()
        {
            T? value = Interlocked.Exchange(ref this.pending, null);
            if (value != null)
            {
                Interlocked.Increment(ref this.delivered);
            }

            return value;
        }

        public void Reset()
        {
            Volatile.Write(ref this.pending, null);
            Interlocked.Exchange(ref this.produced, 0);
            Interlocked.Exchange(ref this.delivered, 0);
        }
    }
}
