// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

/// <summary>
/// Passively displays raw VPW or CAN traffic. Holds the connection lease for the whole capture, so
/// normal operations wait. Behaviour comes from the shared <see cref="BusMonitor"/>.
/// </summary>
public partial record BusMonitorModel
{
    // A large PCM read is ~500k frames; keep a bounded tail rather than the whole capture.
    private const int MaxLines = 5000;

    private readonly IConnectionService connectionService;
    private readonly LoggerAdapter logger;
    private readonly IPromptService promptService;
    private readonly IDispatcher dispatcher;

    private CancellationTokenSource? tokenSource;
    private readonly Queue<string> lines = new Queue<string>();
    private readonly object linesLock = new object();

    public string Title => "Bus Monitor";

    public IState<string> Traffic => State<string>.Value(this, () => string.Empty);
    public IState<string> Status => State<string>.Value(this, () => "Stopped.");
    public IState<string> StartStopButtonText => State<string>.Value(this, () => "Start");
    public IState<bool> IsRunning => State<bool>.Value(this, () => false);
    public IState<bool> IsStopped => State<bool>.Value(this, () => true);

    /// <summary>The CAN acceptance filter, as the shared monitor formats and parses it.</summary>
    public IState<string> CanFilter => State<string>.Value(this, () => BusMonitor.DefaultCanFilter);

    /// <summary>Which bus to watch. False is VPW, true is CAN.</summary>
    public IState<bool> UseCan => State<bool>.Value(this, () => false)
        .ForEach((value, ct) => this.UseCanChanged(value, ct));
    public IState<bool> UseVpw => State<bool>.Value(this, () => true);

    /// <summary>Whether the connected interface can watch each bus; a VPW-only one cannot do CAN.</summary>
    public IState<bool> CanBusAvailable => State<bool>.Value(this, () => true);
    public IState<bool> VpwBusAvailable => State<bool>.Value(this, () => true);

    public BusMonitorModel(
        IConnectionService connectionService,
        LoggerAdapter logger,
        IPromptService promptService,
        IDispatcher dispatcher)
    {
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        var _ = this.RefreshCapability();
    }

    private async ValueTask UseCanChanged(bool useCan, CancellationToken ct)
    {
        await this.UseVpw.SetAsync(!useCan, ct);
    }

    /// <summary>
    /// Find which buses this interface can watch and keep the selection on one. An unusable bus looks
    /// identical to a silent one otherwise.
    /// </summary>
    private async Task RefreshCapability()
    {
        IReadOnlyList<BusProtocol> supported = Array.Empty<BusProtocol>();
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Checking interface", true))
            {
                if (lease != null)
                {
                    supported = lease.Vehicle.MonitorableProtocols;
                }
            }
        }
        catch (Exception exception)
        {
            this.logger.AddDebugMessage("Bus monitor: unable to check the interface. " + exception.Message);
            return;
        }

        bool vpw = supported.Contains(BusProtocol.Vpw);
        bool can = supported.Contains(BusProtocol.Can500k);
        await this.VpwBusAvailable.SetAsync(vpw);
        await this.CanBusAvailable.SetAsync(can);

        if (!vpw && can)
        {
            await this.UseCan.SetAsync(true);
        }
        else if (!can && await this.UseCan.Value() == true)
        {
            await this.UseCan.SetAsync(false);
        }

        if (!vpw && !can)
        {
            await this.Status.SetAsync("This interface cannot monitor either bus.");
        }
    }

    [Command]
    public async ValueTask StartStop(CancellationToken ct)
    {
        if (await this.IsRunning.Value() == true)
        {
            this.tokenSource?.Cancel();
            return;
        }

        await this.Run();
    }

    private async Task Run()
    {
        bool useCan = await this.UseCan.Value() == true;
        BusProtocol protocol = useCan ? BusProtocol.Can500k : BusProtocol.Vpw;
        IReadOnlyCollection<uint>? canIds = useCan
            ? BusMonitor.ParseCanIds(await this.CanFilter.Value())
            : null;

        this.tokenSource = new CancellationTokenSource();
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Bus Monitor", true))
            {
                if (lease == null)
                {
                    await this.Status.SetAsync("Not connected.");
                    return;
                }

                // The 4X rule and its wording live in the library; this only shows the result.
                if (protocol == BusProtocol.Vpw)
                {
                    switch (BusMonitor.CheckVpwReadiness(lease.Vehicle, out string readiness))
                    {
                        case VpwMonitorReadiness.NoFourXSupport:
                            this.logger.AddUserMessage("Bus monitor: " + readiness);
                            break;

                        case VpwMonitorReadiness.FourXDisabled:
                            await this.promptService.Alert(readiness, "Bus Monitor");
                            return;
                    }
                }

                await this.SetRunning(true);
                await this.Status.SetAsync($"Monitoring {protocol}...");
                this.logger.AddUserMessage("Bus monitor started on " + protocol + ".");

                BusMonitor monitor = lease.Vehicle.CreateBusMonitor();
                await Task.Run(() => monitor.RunAsync(protocol, canIds, this.AppendLine, this.tokenSource.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Stop was clicked; not an error.
        }
        catch (Exception exception)
        {
            this.logger.AddUserMessage("Bus monitor error: " + exception.Message);
            this.logger.AddDebugMessage(exception.ToString());
            await this.Status.SetAsync("Bus monitor error: " + exception.Message);
        }
        finally
        {
            this.tokenSource?.Dispose();
            this.tokenSource = null;
            await this.SetRunning(false);
            this.logger.AddUserMessage("Bus monitor stopped.");
        }
    }

    private async Task SetRunning(bool running)
    {
        await this.IsRunning.SetAsync(running);
        await this.IsStopped.SetAsync(!running);
        await this.StartStopButtonText.SetAsync(running ? "Stop" : "Start");
        if (!running)
        {
            await this.Status.SetAsync("Stopped.");
        }
    }

    /// <summary>Called on the capture thread for every frame, so it only queues.</summary>
    private void AppendLine(string line)
    {
        string text;
        lock (this.linesLock)
        {
            this.lines.Enqueue(line);
            while (this.lines.Count > MaxLines)
            {
                this.lines.Dequeue();
            }

            text = string.Join(Environment.NewLine, this.lines);
        }

        var _ = this.dispatcher.ExecuteAsync(async ct => await this.Traffic.SetAsync(text, ct));
    }

    [Command]
    public async ValueTask Clear(CancellationToken ct)
    {
        lock (this.linesLock)
        {
            this.lines.Clear();
        }

        await this.Traffic.SetAsync(string.Empty, ct);
    }

    public void NavigatedAway()
    {
        this.tokenSource?.Cancel();
    }
}
