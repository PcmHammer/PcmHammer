// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using PcmHacking;

namespace PcmHacking
{
    public class BluetoothPort(BluetoothDeviceInfo bluetoothDeviceInfo) : IPort
    {
        private BluetoothClient? _connectedDevice = null;
        private BluetoothDeviceInfo _deviceInfo = bluetoothDeviceInfo;
        private NetworkStream? _deviceStream = null;
        private ConcurrentQueue<byte> _incomingQueue = new();
        private CancellationTokenSource _cancellationTokenSource = new();
        private Task? _ReceiverTask = null;
        private int _packetTimeout = 3000;
        // 2s was not enough for a real adapter to finish pairing/connecting.
        private int _connectionFailTimeout = 6000;
        private bool _localDebug = false;

        public async Task DiscardBuffers()
        {
            _incomingQueue = new ConcurrentQueue<byte>();
            if(_localDebug) Debug.WriteLine($"Flushed BT Buffers={_incomingQueue.Count}");
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _connectedDevice?.Close();
            _connectedDevice?.Dispose();
        }

        public async Task<int> GetReceiveQueueSize()
        {
            return _incomingQueue.Count;
        }

        public async Task OpenAsync(PortConfiguration configuration)
        {
            if(_connectedDevice != null) return;
            _connectedDevice = new BluetoothClient();
            try
            {
                if (!_connectedDevice.Connected)
                {
                    Debug.WriteLine($"Attempting to connect to Bluetooth device {_deviceInfo.DeviceName} at address {_deviceInfo.DeviceAddress}...");
                    await _connectedDevice.ConnectAsync(_deviceInfo.DeviceAddress, BluetoothService.SerialPort).AwaitWithTimeout(TimeSpan.FromMilliseconds(_connectionFailTimeout));
                }
                if (_connectedDevice != null && _connectedDevice.Connected)
                {
                    _deviceStream = _connectedDevice.GetStream();
                    _incomingQueue = new ConcurrentQueue<byte>();
                    _ReceiverTask = new Task(ReceiverTask, _cancellationTokenSource.Token);
                    _ReceiverTask.Start();
                    return;
                }
                _connectedDevice?.Dispose();
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error connecting to Bluetooth device {_deviceInfo.DeviceName}: {ex.Message}");
            }
            throw new IOException($"Connection attempt to Bluetooth device {_deviceInfo.DeviceName} failed!");
        }

        public async Task<int> Receive(byte[] buffer, int offset, int count)
        {
            DateTime startTime = DateTime.Now;
            while (await GetReceiveQueueSize() == 0)
            {
                // Must yield, not block. Thread.Sleep here holds a thread-pool thread, and the receiver
                // task below needs one to resume from its awaited ReadAsync - on Android, where the
                // pool is small and grows slowly, that starves the reader and every block times out.
                await Task.Delay(10);
                if ((DateTime.Now - startTime).TotalMilliseconds > _packetTimeout)
                {
                    throw new TimeoutException();
                }
            }
            int bytesServed = 0;
            while (bytesServed < count && _incomingQueue.Count > 0)
            {
                byte dequeuedByte;
                if(_incomingQueue.TryDequeue(out dequeuedByte))
                {
                    buffer[offset + bytesServed] = dequeuedByte;
                    bytesServed++;
                }
            }
            if (_localDebug) Debug.WriteLine($"Read: New bytes={buffer.ToHex(count)};Len={count}@Offset={offset}");
            return bytesServed;
        }

        public async Task Send(byte[] buffer)
        {
            if (_localDebug) Debug.WriteLine($"Sending bytes={buffer.ToHex()}");
            if(_deviceStream == null) {
                throw new IOException("Bluetooth device stream is null.");
            }
            await _deviceStream.WriteAsync(buffer);
            await _deviceStream.FlushAsync().AwaitWithTimeout(TimeSpan.FromMilliseconds(_packetTimeout));
        }

        public void SetTimeout(int milliseconds)
        {
            // NetorkStream nor MemoryStream natively support timeouts.
            // I neeeded to emulate a read timeout seen above.
            _packetTimeout = milliseconds + 1000;
        }

        public override string ToString()
        {
            // This string is the display for UI, as well as the "(BT)" used
            // as the trigger for creating a BluetoothPort.
            return $"{_deviceInfo.DeviceName}";
        }

        private async void ReceiverTask()
        {
            // A single read failure used to cancel the port's token source, which ended this loop for
            // good: the port stayed open but received nothing ever again, so every later operation
            // timed out with nothing in the log to explain it. Dispose shares that token source, so
            // the port could not recover either. One failed read is usually transient - only a run of
            // them means the link is really gone.
            const int maxConsecutiveErrors = 10;
            int consecutiveErrors = 0;

            while (!_cancellationTokenSource.IsCancellationRequested && _deviceStream != null)
            {
                if (_deviceStream.CanRead)
                {
                    byte[] incomingData = new byte[10000];
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await _deviceStream.ReadAsync(incomingData); // Read all available bytes.
                        consecutiveErrors = 0;
                    }
                    catch (Exception ex)
                    {
                        // Logged unconditionally: gated behind _localDebug, a dying port left no trace.
                        consecutiveErrors++;
                        Debug.WriteLine($"Error reading from Bluetooth device {_deviceInfo.DeviceName} ({consecutiveErrors}/{maxConsecutiveErrors}): {ex.Message}");

                        if (consecutiveErrors >= maxConsecutiveErrors)
                        {
                            Debug.WriteLine($"Giving up on Bluetooth device {_deviceInfo.DeviceName} after {consecutiveErrors} consecutive read failures.");
                            _cancellationTokenSource.Cancel();
                            return;
                        }

                        await Task.Delay(50);
                        continue;
                    }
                    for (int i = 0; i < bytesRead; i++)
                    {
                        _incomingQueue.Enqueue(incomingData[i]);
                    }
                    if (_localDebug) Debug.WriteLine($"Incoming bytes: {incomingData.ToHex(bytesRead)} ReadLen={bytesRead};BufLen={_incomingQueue.Count}");
                }

                // Yield rather than block: this runs on a thread-pool thread, and on Android the pool
                // is small and grows slowly, so holding one here starves the rest of the transfer.
                await Task.Delay(1);
            }
        }

        public Task ChangeBaudRate(int baudRate)
        {
            return Task.CompletedTask;
        }
    }
}
