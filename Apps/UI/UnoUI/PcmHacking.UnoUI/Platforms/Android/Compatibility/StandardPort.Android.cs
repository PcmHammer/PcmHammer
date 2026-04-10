#if ANDROID
using System.Diagnostics;
using System.IO.Ports;

namespace PcmHacking;

public class StandardPort : IPort
{
    private readonly string name;
    private SerialPort? port;
    private Action<byte[], int>? dataReceived;

    public StandardPort(string name)
    {
        this.name = name;
    }

    public override string ToString()
    {
        return this.name;
    }

    Task IPort.OpenAsync(PortConfiguration configuration)
    {
        if (this.port != null)
        {
            this.port.Dispose();
        }

        SerialPortConfiguration config = (SerialPortConfiguration)configuration;
        this.port = new SerialPort(this.name)
        {
            BaudRate = config.BaudRate,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            ReadBufferSize = 12000,
            WriteBufferSize = 12000
        };

        if (config.Timeout == 0)
        {
            config.Timeout = 1000;
        }

        this.port.ReadTimeout = config.Timeout;
        if (this.port.IsOpen)
        {
            this.port.Close();
        }

        this.port.Open();
        this.port.BaseStream.ReadTimeout = this.port.ReadTimeout;

        if (config.DataReceived != null)
        {
            this.dataReceived = config.DataReceived;
            Task.Run(this.Receiver);
        }

        return Task.CompletedTask;
    }

    private async Task Receiver()
    {
        if (this.port == null)
        {
            return;
        }

        byte[] buffer = new byte[100];
        while (this.port != null)
        {
            try
            {
                int bytesReceived = await this.port.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesReceived > 0 && this.dataReceived != null)
                {
                    this.dataReceived(buffer, bytesReceived);
                }
            }
            catch (Exception exception) when (exception is not ObjectDisposedException)
            {
                Debug.WriteLine("StandardPort.Receiver: " + exception);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public Task ChangeBaudRate(int baudRate)
    {
        this.port.BaudRate = baudRate;
        this.port.DiscardInBuffer();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        this.port?.Dispose();
        this.port = null;
    }

    async Task IPort.Send(byte[] buffer)
    {
        if (this.port == null)
        {
            throw new InvalidOperationException("Port is not open.");
        }

        await this.port.BaseStream.WriteAsync(buffer, 0, buffer.Length).AwaitWithTimeout(TimeSpan.FromSeconds(5));
        await this.port.BaseStream.FlushAsync().AwaitWithTimeout(TimeSpan.FromSeconds(5));
    }

    Task<int> IPort.Receive(byte[] buffer, int offset, int count)
    {
        if (this.port == null)
        {
            return Task.FromResult(0);
        }

        try
        {
            return TimeoutUtilities.TaskWithTimeoutAndException(
                Task.Run(() => this.port.Read(buffer, offset, count)),
                TimeSpan.FromMilliseconds(this.port.ReadTimeout));
        }
        catch (TimeoutException)
        {
            return Task.FromResult(0);
        }
    }

    public Task DiscardBuffers()
    {
        if (this.port != null)
        {
            this.port.DiscardInBuffer();
            this.port.DiscardOutBuffer();
        }

        return Task.CompletedTask;
    }

    public void SetTimeout(int milliseconds)
    {
        if (this.port != null)
        {
            this.port.ReadTimeout = milliseconds;
        }
    }

    Task<int> IPort.GetReceiveQueueSize()
    {
        return Task.FromResult(this.port?.BytesToRead ?? 0);
    }
}
#endif
