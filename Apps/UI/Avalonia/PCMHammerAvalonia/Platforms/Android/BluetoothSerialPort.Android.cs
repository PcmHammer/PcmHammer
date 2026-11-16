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

    // Temporary diagnostic hook: when set, raw TX/RX bytes are reported here (wired to the in-app Debug
    // log) so the Bluetooth write handshakes can be traced on-device without a USB logcat. Remove once
    // the write path is proven.
    public static Action<string>? Trace;

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

        // OpenAsync is called more than once per connection: first by the detector with the probe
        // configuration, then again by the chosen device's Initialize() with its real serial settings.
        // A wired serial port just reconfigures in place, but a Bluetooth adapter accepts only ONE
        // RFCOMM link at a time - opening a second socket while the first is still live is refused with
        // "read failed ... read ret: -1". Baud rate and the other serial settings are meaningless over
        // RFCOMM, so when the socket is already connected we keep it and only refresh the timeout.
        if (this.socket != null && this.socket.IsConnected)
        {
            return;
        }

        BluetoothAdapter adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("No Bluetooth adapter on this device.");
        adapter.CancelDiscovery();

        BluetoothDevice device = adapter.GetRemoteDevice(this.address)
            ?? throw new InvalidOperationException($"Bluetooth device {this.address} not found.");

        // Connect blocks, so do it off the UI thread.
        this.socket = await Task.Run(() => ConnectWithFallback(device));

        this.input = this.socket.InputStream;
        this.output = this.socket.OutputStream;
        this.cts = new CancellationTokenSource();
        _ = Task.Run(() => this.Receiver(this.cts.Token, config.DataReceived));
    }

    // Android RFCOMM is finicky: the standard secure socket throws "read failed, socket might closed or
    // timeout, read ret: -1" against many OBD adapters even when they are powered and paired. Try the
    // secure socket first (works on most), then an insecure socket, then the long-standing reflection
    // "createRfcommSocket(channel 1)" workaround for adapters whose SPP service-record lookup fails.
    private BluetoothSocket ConnectWithFallback(BluetoothDevice device)
    {
        List<string> failures = new();

        if (TryConnect(() => device.CreateRfcommSocketToServiceRecord(SppUuid), "secure SPP", failures, out BluetoothSocket? s))
        {
            return s!;
        }

        if (TryConnect(() => device.CreateInsecureRfcommSocketToServiceRecord(SppUuid), "insecure SPP", failures, out s))
        {
            return s!;
        }

        if (TryConnect(() => CreateReflectionSocket(device), "reflection channel 1", failures, out s))
        {
            return s!;
        }

        throw new System.IO.IOException(
            $"Could not open a Bluetooth connection to {this.address}. Tried {string.Join("; ", failures)}. "
            + "Make sure the adapter is powered (plugged into the vehicle, key on) and paired.");
    }

    private static bool TryConnect(Func<BluetoothSocket?> create, string label, List<string> failures, out BluetoothSocket? socket)
    {
        socket = null;
        try
        {
            socket = create() ?? throw new InvalidOperationException("socket was null");
            socket.Connect();
            Debug.WriteLine($"BluetoothSerialPort: connected via {label}.");
            return true;
        }
        catch (Exception exception)
        {
            failures.Add($"{label}: {exception.Message}");
            Debug.WriteLine($"BluetoothSerialPort: {label} failed: {exception}");
            try { socket?.Close(); } catch { /* ignore */ }
            try { socket?.Dispose(); } catch { /* ignore */ }
            socket = null;
            return false;
        }
    }

    // device.getClass().getMethod("createRfcommSocket", int.class).invoke(device, 1)
    private static BluetoothSocket? CreateReflectionSocket(BluetoothDevice device)
    {
        Java.Lang.Reflect.Method method = device.Class.GetMethod("createRfcommSocket", Java.Lang.Integer.Type)!;
        return method.Invoke(device, Java.Lang.Integer.ValueOf(1)) as BluetoothSocket;
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
                    Trace?.Invoke($"RX-chunk[{read}] {BitConverter.ToString(buffer, 0, read)}");
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

        Trace?.Invoke($"TX[{buffer.Length}] {BitConverter.ToString(buffer)}");
        await this.output.WriteAsync(buffer, 0, buffer.Length);
        await this.output.FlushAsync();
    }

    // Baud rate is meaningless over Bluetooth RFCOMM.
    public Task ChangeBaudRate(int baudRate) => Task.CompletedTask;

    // Inter-byte idle (ms) tolerated once a reply has started. Bluetooth RFCOMM delivers a reply in
    // bursts with small gaps, so returning the instant the queue is momentarily empty truncates it
    // (e.g. "OBDX..." arrives as "OBD" then "X..."). Wait for the sender to actually pause before
    // concluding the reply is complete; the overall timeout still bounds the wait for the first byte.
    private const int InterByteIdleMs = 100;

    async Task<int> IPort.Receive(byte[] buffer, int offset, int count)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int got = 0;
        long lastByteAtMs = 0;
        while (got < count)
        {
            if (this.incoming.TryDequeue(out byte value))
            {
                buffer[offset + got] = value;
                got++;
                lastByteAtMs = stopwatch.ElapsedMilliseconds;
            }
            else if (got > 0)
            {
                // Got part of a reply: wait out a short inter-byte gap before giving up, so a reply
                // split across RFCOMM packets is returned whole rather than truncated at the first gap.
                if (stopwatch.ElapsedMilliseconds - lastByteAtMs >= InterByteIdleMs)
                {
                    break;
                }

                await Task.Delay(1);
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

        Trace?.Invoke($"RX-return got={got}/{count} {BitConverter.ToString(buffer, offset, got)}");
        return got;
    }

    public async Task DiscardBuffers()
    {
        // Let in-flight bytes land before clearing. Over Bluetooth a reply's trailing ELM ">" prompt
        // arrives a few tens of ms late - after the caller has already discarded - so without this wait
        // the stale prompt survives into the next probe's read and the identify replies end up shifted
        // by one probe (the adapter answers "OBDX Pro VT" but the OBDX check reads a leftover ">").
        await Task.Delay(80);
        while (this.incoming.TryDequeue(out _))
        {
        }
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
