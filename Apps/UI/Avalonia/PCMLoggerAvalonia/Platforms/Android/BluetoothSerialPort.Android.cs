// SPDX-License-Identifier: GPL-3.0-only
// Android Bluetooth RFCOMM/SPP transport. The usual way a phone reaches an OBD adapter: connect to a
// paired device's Serial Port Profile and present it to the shared SerialDeviceDetector as an IPort,
// exactly like a wired serial port. (System.IO.Ports can only reach a USB-OTG tty, often root-only, so
// Bluetooth is the practical mobile path.)
#if ANDROID
using System.Collections.Concurrent;
using System.Diagnostics;
using Android.Bluetooth;
using Java.Util;

namespace PcmHacking;

public class BluetoothSerialPort : IPort
{
    // Standard Serial Port Profile (SPP) UUID.
    private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    private readonly string address;
    private BluetoothSocket? socket;
    private System.IO.Stream? input;
    private System.IO.Stream? output;
    private readonly ConcurrentQueue<byte> incoming = new();
    private CancellationTokenSource cts = new();
    private int timeoutMs = 1000;

    public BluetoothSerialPort(string address)
    {
        this.address = address;
    }

    public override string ToString() => this.address;

    /// <summary>The paired (bonded) Bluetooth devices, for the mobile device picker.</summary>
    public static IReadOnlyList<(string Name, string Address)> GetPairedDevices()
    {
        BluetoothAdapter? adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter?.BondedDevices == null)
        {
            return Array.Empty<(string, string)>();
        }

        List<(string, string)> devices = new();
        foreach (BluetoothDevice device in adapter.BondedDevices)
        {
            string addr = device.Address ?? string.Empty;
            if (addr.Length > 0)
            {
                devices.Add((device.Name ?? addr, addr));
            }
        }

        return devices;
    }

    async Task IPort.OpenAsync(PortConfiguration configuration)
    {
        SerialPortConfiguration config = (SerialPortConfiguration)configuration;
        this.timeoutMs = config.Timeout > 0 ? config.Timeout : 1000;

        BluetoothAdapter adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("No Bluetooth adapter on this device.");
        adapter.CancelDiscovery();

        BluetoothDevice device = adapter.GetRemoteDevice(this.address)
            ?? throw new InvalidOperationException($"Bluetooth device {this.address} not found.");

        this.socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Could not create an RFCOMM socket.");

        // Connect blocks, so do it off the UI thread.
        await Task.Run(() => this.socket.Connect());

        this.input = this.socket.InputStream;
        this.output = this.socket.OutputStream;
        this.cts = new CancellationTokenSource();
        _ = Task.Run(() => this.Receiver(this.cts.Token, config.DataReceived));
    }

    private async Task Receiver(CancellationToken token, Action<byte[], int>? dataReceived)
    {
        byte[] buffer = new byte[256];
        while (!token.IsCancellationRequested && this.input != null)
        {
            try
            {
                int read = await this.input.ReadAsync(buffer, 0, buffer.Length, token);
                if (read > 0)
                {
                    if (dataReceived != null)
                    {
                        dataReceived(buffer, read);
                    }
                    else
                    {
                        for (int i = 0; i < read; i++)
                        {
                            this.incoming.Enqueue(buffer[i]);
                        }
                    }
                }
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                Debug.WriteLine("BluetoothSerialPort.Receiver: " + exception);
                break;
            }
            catch
            {
                break;
            }
        }
    }

    async Task IPort.Send(byte[] buffer)
    {
        if (this.output == null)
        {
            throw new InvalidOperationException("Port is not open.");
        }

        await this.output.WriteAsync(buffer, 0, buffer.Length);
        await this.output.FlushAsync();
    }

    // Baud rate is meaningless over Bluetooth RFCOMM.
    public Task ChangeBaudRate(int baudRate) => Task.CompletedTask;

    async Task<int> IPort.Receive(byte[] buffer, int offset, int count)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int got = 0;
        while (got < count)
        {
            if (this.incoming.TryDequeue(out byte value))
            {
                buffer[offset + got] = value;
                got++;
            }
            else if (got > 0)
            {
                break;
            }
            else if (stopwatch.ElapsedMilliseconds >= this.timeoutMs)
            {
                break;
            }
            else
            {
                await Task.Delay(1);
            }
        }

        return got;
    }

    public Task DiscardBuffers()
    {
        while (this.incoming.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public void SetTimeout(int milliseconds) => this.timeoutMs = milliseconds;

    Task<int> IPort.GetReceiveQueueSize() => Task.FromResult(this.incoming.Count);

    public void Dispose()
    {
        try
        {
            this.cts.Cancel();
        }
        catch
        {
        }

        try
        {
            this.socket?.Close();
        }
        catch
        {
        }

        this.socket?.Dispose();
        this.socket = null;
        this.input = null;
        this.output = null;
    }
}
#endif
