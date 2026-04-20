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
    public class BluetoothPort : IPort
    {
        private BluetoothClient _connectedDevice;
        private BluetoothDeviceInfo _deviceInfo;
        private NetworkStream _deviceStream;
        private ConcurrentQueue<byte> _incomingQueue;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _ReceiverTask;
        private int _readTimeout = 3000;
        private bool _localDebug = true;

        public BluetoothPort(BluetoothDeviceInfo bluetoothDeviceInfo)
        {
            _deviceInfo = bluetoothDeviceInfo;
            _cancellationTokenSource = new();
        }

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
                    await _connectedDevice.ConnectAsync(_deviceInfo.DeviceAddress, BluetoothService.SerialPort);
                }
                if (_connectedDevice != null && _connectedDevice.Connected)
                {
                    _deviceStream = _connectedDevice.GetStream();
                    _incomingQueue = new ConcurrentQueue<byte>();
                    _ReceiverTask = new Task(ReceiverTask, _cancellationTokenSource.Token);
                    _ReceiverTask.Start();
                    return;
                }
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
                await Task.Delay(10);
                if ((DateTime.Now - startTime).TotalMilliseconds > _readTimeout)
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
            await _deviceStream.WriteAsync(buffer);
            await _deviceStream.FlushAsync();
        }

        public void SetTimeout(int milliseconds)
        {
            // NetorkStream nor MemoryStream natively support timeouts.
            // I neeeded to emulate a read timeout seen above.
            _readTimeout = milliseconds + 1000;
        }

        public override string ToString()
        {
            // This string is the display for UI, as well as the "(BT)" used
            // as the trigger for creating a BluetoothPort.
            return $"{_deviceInfo.DeviceName}";
        }

        private async void ReceiverTask()
        {
            while (!_cancellationTokenSource.IsCancellationRequested && _deviceStream != null)
            {
                if (_deviceStream.CanRead)
                {
                    byte[] incomingData = new byte[10000];
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await _deviceStream.ReadAsync(incomingData); // Read all available bytes.
                    }
                    catch (Exception ex)
                    {
                        if (_localDebug) Debug.WriteLine($"Error reading from Bluetooth device {_deviceInfo.DeviceName}: {ex.Message}");
                    }
                    for (int i = 0; i < bytesRead; i++)
                    {
                        _incomingQueue.Enqueue(incomingData[i]);
                    }
                    if (_localDebug) Debug.WriteLine($"Incoming bytes: {incomingData.ToHex(bytesRead)} ReadLen={bytesRead};BufLen={_incomingQueue.Count}");
                }
                Thread.Sleep(1);
            }
        }

        public Task ChangeBaudRate(int baudRate)
        {
            return Task.CompletedTask;
        }
    }
}
