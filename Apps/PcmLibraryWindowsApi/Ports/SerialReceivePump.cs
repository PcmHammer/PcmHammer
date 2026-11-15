// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace PcmHacking
{
    /// <summary>
    /// Continuously moves received bytes from a SerialPort into a buffer owned by this process.
    ///
    /// On Linux the tty layer holds at most 4095 unread bytes and SerialPort.ReadBufferSize does not
    /// raise that limit, so BytesToRead can never report a larger frame. Device code that waits for
    /// a whole frame to be queued (an OBDX Pro 4096-byte read block needs 4109) would wait forever.
    /// Draining the tty as data arrives also stops the kernel from throttling the USB transfer.
    ///
    /// The thread polls BytesToRead rather than blocking in Read, so no read is in flight when the
    /// port is idle and being disposed (see StandardPort.SafeDisposeSerialPort for why that matters).
    /// </summary>
    internal sealed class SerialReceivePump
    {
        // Comfortably above the largest DVI frame (64KiB). When full, the pump stops reading and
        // bytes wait in the kernel, as they do without the pump.
        private const int Capacity = 256 * 1024;
        private const int ChunkSize = 4096;

        // Upper bound on how long a waiting Read goes without noticing Stop().
        private const int WaitSliceMilliseconds = 50;

        private readonly SerialPort port;
        private readonly object sync = new object();
        private readonly byte[] buffer = new byte[Capacity];
        private int head;
        private int count;
        private Exception? fault;
        private volatile bool stopped;

        public SerialReceivePump(SerialPort port)
        {
            this.port = port;
            Thread thread = new Thread(this.Run)
            {
                IsBackground = true,
                Name = "Serial receive pump"
            };
            thread.Start();
        }

        /// <summary>
        /// Stop the pump. Does not take the lock, so a hung port cannot block the caller.
        /// </summary>
        public void Stop()
        {
            this.stopped = true;
        }

        /// <summary>
        /// Bytes waiting to be read. Throws if the port failed and nothing is left to read, as
        /// BytesToRead would on a dead port.
        /// </summary>
        public int Count
        {
            get
            {
                lock (this.sync)
                {
                    if (this.count == 0 && this.fault != null)
                    {
                        throw new IOException("Serial port read failed.", this.fault);
                    }

                    return this.count;
                }
            }
        }

        /// <summary>
        /// Same contract as SerialPort.Read, except that a timeout returns 0 rather than throwing:
        /// waits for at least one byte, then returns what is available, up to the requested count.
        /// A negative timeout waits indefinitely.
        /// </summary>
        public int Read(byte[] destination, int offset, int requested, int timeoutMilliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew();

            lock (this.sync)
            {
                while (this.count == 0)
                {
                    if (this.stopped || this.fault != null)
                    {
                        return 0;
                    }

                    int wait = WaitSliceMilliseconds;
                    if (timeoutMilliseconds >= 0)
                    {
                        long remaining = timeoutMilliseconds - elapsed.ElapsedMilliseconds;
                        if (remaining <= 0)
                        {
                            return 0;
                        }

                        wait = (int)Math.Min(remaining, WaitSliceMilliseconds);
                    }

                    Monitor.Wait(this.sync, wait);
                }

                int total = Math.Min(requested, this.count);
                int first = Math.Min(total, Capacity - this.head);
                Buffer.BlockCopy(this.buffer, this.head, destination, offset, first);
                Buffer.BlockCopy(this.buffer, 0, destination, offset + first, total - first);
                this.head = (this.head + total) % Capacity;
                this.count -= total;
                return total;
            }
        }

        /// <summary>
        /// Run an operation that discards the port's OS buffers, and empty this buffer with it. The
        /// pump cannot read during the operation, so no pre-discard bytes can reappear afterwards.
        /// </summary>
        public void Discard(Action discardPortBuffers)
        {
            lock (this.sync)
            {
                discardPortBuffers();
                this.head = 0;
                this.count = 0;
            }
        }

        private void Run()
        {
            byte[] chunk = new byte[ChunkSize];

            while (!this.stopped)
            {
                bool received = false;

                try
                {
                    lock (this.sync)
                    {
                        int room = Capacity - this.count;
                        int available = room > 0 ? this.port.BytesToRead : 0;
                        if (available > 0)
                        {
                            int read = this.port.Read(chunk, 0, Math.Min(Math.Min(available, room), ChunkSize));
                            if (read > 0)
                            {
                                int tail = (this.head + this.count) % Capacity;
                                int first = Math.Min(read, Capacity - tail);
                                Buffer.BlockCopy(chunk, 0, this.buffer, tail, first);
                                Buffer.BlockCopy(chunk, first, this.buffer, 0, read - first);
                                this.count += read;
                                received = true;
                                Monitor.PulseAll(this.sync);
                            }
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // Nothing arrived after all. Keep going.
                }
                catch (Exception exception)
                {
                    // The port is gone or unusable. Record it, wake any reader, and end the thread.
                    lock (this.sync)
                    {
                        this.fault = exception;
                        Monitor.PulseAll(this.sync);
                    }

                    return;
                }

                if (!received)
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}
