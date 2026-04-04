using System;
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
        private MemoryStream _incomingMemoryBuffer;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _ReceiverTask;
        private int _readTimeout = 1000;
        private bool _localDebug = false;
        private bool _receiveBlocked = false;

        public BluetoothPort(BluetoothDeviceInfo bluetoothDeviceInfo)
        {
            _deviceInfo = bluetoothDeviceInfo;
            _cancellationTokenSource = new();
        }

        public async Task DiscardBuffers()
        {
            _incomingMemoryBuffer = new();
            if(_localDebug) Debug.WriteLine($"Flushed BT Buffers={_incomingMemoryBuffer.Length - _incomingMemoryBuffer.Position};Len={_incomingMemoryBuffer.Length};Pos={_incomingMemoryBuffer.Position}");
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _connectedDevice?.Dispose();
            _incomingMemoryBuffer.Dispose();
        }

        public async Task<int> GetReceiveQueueSize()
        {
            return (int)(_incomingMemoryBuffer.Length - _incomingMemoryBuffer.Position);
        }

        public async Task OpenAsync(PortConfiguration configuration)
        {
            _connectedDevice = new BluetoothClient();
            await _connectedDevice.ConnectAsync(_deviceInfo.DeviceAddress, BluetoothService.SerialPort);
            if (_connectedDevice != null)
            {
                _deviceStream = _connectedDevice.GetStream();
                _incomingMemoryBuffer = new();
                Thread.Sleep(1000);
                _ReceiverTask = new Task(ReceiverTask, _cancellationTokenSource.Token);
                _ReceiverTask.Start();
                return;
            }
            throw new IOException($"Connection attempt to Bluetooth device {_deviceInfo.DeviceName} failed!");
        }

        public async Task<int> Receive(byte[] buffer, int offset, int count)
        {
            DateTime startTime = DateTime.Now;
            while (await GetReceiveQueueSize() < count || _receiveBlocked)
            {
                await Task.Delay(10);
                if ((DateTime.Now - startTime).TotalMilliseconds > _readTimeout)
                {
                    throw new TimeoutException();
                }
            }
            if (offset == 0 && count == 1)
            {
                int incomingByte = _incomingMemoryBuffer.ReadByte();
                if (incomingByte == -1)
                {
                    return 0;
                }
                buffer[0] = (byte)incomingByte;
                return 1;
            }
            int bytesRead = await _incomingMemoryBuffer?.ReadAsync(buffer, offset, count);
            if (_localDebug) Debug.WriteLine($"Read: New bytes={buffer.ToHex(count)};Len={count}@Offset={offset}");
            return bytesRead;
        }

        public async Task Send(byte[] buffer)
        {
            if (_localDebug) Debug.WriteLine($"Sending bytes={buffer.ToHex()}");
            await _deviceStream?.WriteAsync(buffer, 0, buffer.Length);
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
            return $"{_deviceInfo.DeviceName}(BT)";
        }

        private async void ReceiverTask()
        {
            while (!_cancellationTokenSource.IsCancellationRequested && _deviceStream != null)
            {
                if (_deviceStream.DataAvailable && _deviceStream.CanRead)
                {
                    try
                    {
                        _receiveBlocked = true;
                        byte[] incomingData = new byte[10000];
                        int prevBuffLen = (int)_incomingMemoryBuffer.Length;
                        long prevBuffPos = _incomingMemoryBuffer.Position;
                        int TotalbytesRead = 0;
                        _incomingMemoryBuffer.Position = prevBuffLen; // Set position to end
                        while (_deviceStream.DataAvailable)
                        {
                            int bytesRead = await _deviceStream.ReadAsync(incomingData, 0, incomingData.Length); // Read all available bytes
                            await _incomingMemoryBuffer.WriteAsync(incomingData, 0, bytesRead); // Push read bytes to end of buffer.
                            TotalbytesRead += bytesRead;
                            //await Task.Delay(_partialPacketDelay);
                        }
                        _incomingMemoryBuffer.Position = prevBuffPos; // Reset position to last used.
                        if (_localDebug) Debug.WriteLine($"Incoming bytes: {incomingData.ToHex(TotalbytesRead)} ReadLen={TotalbytesRead};BufLen={_incomingMemoryBuffer.Length};Pos={_incomingMemoryBuffer.Position}");
                    }
                    catch
                    {
                        return;
                    }
                    finally
                    {
                        _receiveBlocked = false;
                    }
                }
                Thread.Sleep(10);
            }
        }

        public Task ChangeBaudRate(int baudRate)
        {
            return Task.CompletedTask;
        }
    }
}
