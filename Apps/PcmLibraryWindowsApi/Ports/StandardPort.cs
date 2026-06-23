// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public delegate void DataReceived();

    /// <summary>
    /// This class is responsible for sending and receiving data over a serial port.
    /// I would have called it 'SerialPort' but that name was already taken...
    /// </summary>
    public class StandardPort : IPort
    {
        private string name;
        private SerialPort? port;

        /// <summary>
        /// This is an experiment that did not end well the first time, but I still think it should work.
        /// </summary>
        // private Action<object, SerialDataReceivedEventArgs> dataReceivedCallback;
        Action<byte[], int>? dataReceived;

        /// <summary>
        /// Constructor.
        /// </summary>
        public StandardPort(string name)
        {
            this.name = name;
        }

        public StandardPort(string name, Action<byte[], int> dataReceived)
        {
            this.name = name;
            this.dataReceived = dataReceived;
        }

        /// <summary>
        /// This returns the string that appears in the drop-down list.
        /// </summary>
        public override string ToString()
        {
            return this.name;
        }

        /// <summary>
        /// Open the serial port.
        /// </summary>
        async Task IPort.OpenAsync(PortConfiguration configuration)
        {
            SerialPortConfiguration config = (SerialPortConfiguration)configuration;
            if (config.Timeout == 0) config.Timeout = 1000; // default to 1 second but allow override.

            // Dispose any previous port and wait (briefly) for the OS to release the handle, so
            // re-opening the same port name succeeds. The AVT flow, for example, opens the port to
            // auto-detect the device and then opens it again to initialize. A live port closes in
            // milliseconds; a defunct/unresponsive port is abandoned after the timeout rather than
            // blocking the caller forever.
            await this.DisposePortAsync(TimeSpan.FromSeconds(3));

            SerialPort newPort = await this.OpenNewPort(config);

            this.port = newPort;

            // This line must come AFTER the call to port.Open().
            // Attempting to use the BaseStream member will throw an exception otherwise.
            //
            // However, even after setting the BaseStream.ReadTimout property, calls to
            // BaseStream.ReadAsync will hang indefinitely. It turns out that you have
            // to implement the timeout yourself if you use the async approach.
            this.port.BaseStream.ReadTimeout = this.port.ReadTimeout;

            if (config.DataReceived != null)
            {
                this.dataReceived = config.DataReceived;
                _ = Task.Run(this.Receiver); // fire-and-forget background receive loop; intentionally not awaited
            }
        }

        /// <summary>
        /// Build and open a fresh SerialPort.
        ///
        /// Open() is run on a worker thread with a hard timeout, because a defunct device can
        /// make it block effectively forever and that must never hang the caller (often the UI
        /// thread). A transient "access denied" - which happens when the previous handle for the
        /// same port is still being released - is retried a few times before giving up.
        /// </summary>
        private async Task<SerialPort> OpenNewPort(SerialPortConfiguration config)
        {
            const int maxAttempts = 4;
            UnauthorizedAccessException? lastDenied = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                SerialPort newPort = new SerialPort(this.name);
                newPort.BaudRate = config.BaudRate;
                newPort.DataBits = 8;
                newPort.Parity = Parity.None;
                newPort.StopBits = StopBits.One;
                newPort.ReadBufferSize = 12000;
                newPort.WriteBufferSize = 12000;
                newPort.ReadTimeout = config.Timeout;

                Task openTask = Task.Run(() => newPort.Open());
                Task firstToFinish = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(5)));
                if (firstToFinish != openTask)
                {
                    // The open is still stuck. Abandon the half-open port without blocking and bail out.
                    // A genuine hang is not retried - that would just multiply the wait.
                    SafeDisposeSerialPort(newPort);
                    throw new TimeoutException($"Timed out trying to open serial port {this.name}.");
                }

                try
                {
                    // Observe the result so a failed open propagates cleanly to the caller.
                    await openTask;
                    return newPort;
                }
                catch (UnauthorizedAccessException denied)
                {
                    // The port is in use - most often by its own previous handle, which is still
                    // closing after a just-finished connection. Back off briefly and try again.
                    SafeDisposeSerialPort(newPort);
                    lastDenied = denied;
                    await Task.Delay(250);
                }
                catch
                {
                    // Any other failure (e.g. IOException for a missing device) is final.
                    SafeDisposeSerialPort(newPort);
                    throw;
                }
            }

            // Ran out of retries while still being denied access.
            throw lastDenied!;
        }

        private async void Receiver()
        {
            // Capture the port this loop belongs to. If the port is replaced or disposed,
            // this.port changes and the loop exits instead of reading from a stale/dead port.
            SerialPort? p = this.port;
            byte[] buffer = new byte[100];
            while (p != null && ReferenceEquals(p, this.port))
            {
                try
                {
                    int bytesReceived = await p.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesReceived > 0)
                    {
                        this.dataReceived?.Invoke(buffer, bytesReceived);
                    }
                }
                catch (Exception exception)
                {
                    // Any error here means the port is gone or unusable. Stop the loop rather than
                    // spinning forever on a dead port (which would peg a CPU core).
                    if (!(exception is ObjectDisposedException))
                    {
                        Debug.WriteLine("StandardPort.DataListener: " + exception.ToString());
                    }

                    break;
                }
            }
        }

        public Task ChangeBaudRate(int baudRate)
        {
            this.port!.BaudRate = baudRate;
            this.port.DiscardInBuffer();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Close the serial port.
        /// </summary>
        public void Dispose()
        {
            this.DisposePort();
        }

        /// <summary>
        /// Detach and dispose the current SerialPort, if any, without ever blocking the caller.
        /// Used for teardown (e.g. switching devices), where nothing reopens this same object.
        /// </summary>
        private void DisposePort()
        {
            // Detach first: clearing this.port stops the Receiver loop and makes every other
            // member treat the port as closed, even before the (possibly slow) Dispose completes.
            SerialPort? p = this.port;
            this.port = null;
            if (p != null)
            {
                SafeDisposeSerialPort(p);
            }
        }

        /// <summary>
        /// Detach the current SerialPort and wait up to <paramref name="timeout"/> for its Dispose
        /// to complete, then return. Used before re-opening: a live port releases its OS handle in
        /// milliseconds (so the re-open succeeds), while a defunct port is abandoned after the
        /// timeout instead of blocking the caller forever.
        /// </summary>
        private async Task DisposePortAsync(TimeSpan timeout)
        {
            SerialPort? p = this.port;
            this.port = null;
            if (p == null)
            {
                return;
            }

            Task disposeTask = Task.Run(() =>
            {
                try
                {
                    p.Dispose();
                }
                catch
                {
                    // A defunct port can throw (or hang) on Dispose; the handle is abandoned.
                }
            });

            // If the dispose hangs (dead port), we proceed without it; the subsequent open will
            // either succeed or fail cleanly, but we will not be stuck here.
            await Task.WhenAny(disposeTask, Task.Delay(timeout));
        }

        /// <summary>
        /// Dispose a SerialPort on a background thread and never wait for it.
        ///
        /// SerialPort.Close()/Dispose() can block indefinitely when the underlying device is
        /// defunct or unresponsive (e.g. a stale Bluetooth COM port): the internal event loop
        /// thread is stuck in a Win32 call that never returns, and Dispose waits to join it.
        /// If that happens on the UI thread the whole app hard-locks. By disposing on a worker
        /// thread and not awaiting it, a dead port can leak its handle but can never freeze us.
        /// </summary>
        private static void SafeDisposeSerialPort(SerialPort p)
        {
            if (p == null)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    p.Dispose();
                }
                catch
                {
                    // A defunct port can throw (or hang) on Dispose. Swallow it - the handle is
                    // abandoned, and there is nothing useful we can do about a dead device.
                }
            });
        }

        /// <summary>
        /// Send a sequence of bytes over the serial port.
        /// </summary>
        async Task IPort.Send(byte[] buffer)
        {

            await this.port!.BaseStream.WriteAsync(buffer, 0, buffer.Length).AwaitWithTimeout(TimeSpan.FromSeconds(5));

            // This flush is probably not strictly necessary, but just in case...
            await this.port.BaseStream.FlushAsync().AwaitWithTimeout(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Receive a sequence of bytes over the serial port.
        /// </summary>
        Task<int> IPort.Receive(byte[] buffer, int offset, int count)
        {
            // Run the port read operation safely on a background thread
            return Task.Run(() =>
            {
                try
                {
                    // If the SerialPort's internal ReadTimeout is reached, it throws a TimeoutException
                    return this.port!.Read(buffer, offset, count);
                }
                catch (TimeoutException)
                {
                    // Safely intercept the hardware timeout and return 0 bytes read
                    return 0;
                }
                catch (Exception ex)
                {
                    return 0;
                }
            });
        }

        /// <summary>
        /// Discard anything in the input and output buffers.
        /// </summary>
        public Task DiscardBuffers()
        {
            this.port!.DiscardInBuffer();
            this.port.DiscardOutBuffer();
            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the read timeout.
        /// </summary>
        public void SetTimeout(int milliseconds)
        {
            this.port!.ReadTimeout = milliseconds;
        }

        /// <summary>
        /// Serial data callback.
        /// </summary>
        private async void Port_DataReceived(object sender, SerialDataReceivedEventArgs args)
        {
            if (args.EventType == SerialData.Chars)
            {
                byte[] buffer = new byte[1000];
                int bytesReceived = await this.port!.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                this.dataReceived?.Invoke(buffer, bytesReceived);

            }
        }

        /// <summary>
        /// Indicates the number of bytes waiting in the queue.
        /// </summary>
        Task<int> IPort.GetReceiveQueueSize()
        {
            return Task.FromResult(this.port!.BytesToRead);
        }
    }
}

