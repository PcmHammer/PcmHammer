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
        Task IPort.OpenAsync(PortConfiguration configuration)
        {
            // Clean up the existing SerialPort object, if we have one.
            if (this.port != null)
            {
                this.port.Dispose();
            }
            SerialPortConfiguration config = (SerialPortConfiguration)configuration;
            this.port = new SerialPort(this.name);
            this.port.BaudRate = config.BaudRate;
            this.port.DataBits = 8;
            this.port.Parity = Parity.None;
            this.port.StopBits = StopBits.One;
            this.port.ReadBufferSize = 12000;
            this.port.WriteBufferSize = 12000;
            if (config.Timeout == 0) config.Timeout = 1000; // default to 1 second but allow override.
            this.port.ReadTimeout = config.Timeout;

            if (this.port.IsOpen == true) this.port.Close();

            this.port.Open();

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
                Task.Run(this.Receiver);
            }

            return Task.CompletedTask;
        }

        private async void Receiver()
        {
            byte[] buffer = new byte[100];
            while(this.port != null)
            {
                try
                {
                    int bytesReceived = await this.port.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesReceived > 0)
                    {
                        this.dataReceived?.Invoke(buffer, bytesReceived);
                    }
                }
                catch(Exception exception)
                {
                    if (exception is ObjectDisposedException)
                    {
                        break;
                    }

                    Debug.WriteLine("StandardPort.DataListener: " + exception.ToString());
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
            if (this.port != null)
            {
                this.port.Dispose();
                this.port = null;
            }
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
            try
            {
                return TimeoutUtilities.TaskWithTimeoutAndException(
                    Task.Run(() => this.port!.Read(buffer, offset, count)),
                    TimeSpan.FromMilliseconds(this.port!.ReadTimeout));
            } catch (TimeoutException) 
            {
                return Task.FromResult(0);
            }
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

