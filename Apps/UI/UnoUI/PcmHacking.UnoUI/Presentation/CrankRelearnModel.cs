using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using Uno.Extensions.Reactive.Commands;
using Windows.Devices.Bluetooth.Background;

namespace PcmHacking.UnoUI.Presentation;

public enum CrankRelearnStates
{
    WaitingForPcm,
    WaitingForUser,
    AllConditionsMet,
    RevUp,
    RevDown,
    Success,
    Failure
}

public partial record CrankRelearnModel()
{
    const string ready = "Ready";
    const string notReady = "Not Ready";
    const string unavailable = "---";

    private readonly INavigator navigator;
    private readonly IConnectionService connectionService;
    private readonly LoggerAdapter progressLogger;
    private readonly DispatcherQueue dispatcherQueue;

    private CancellationTokenSource cancellation = new CancellationTokenSource();
    private CrankRelearnStates state = CrankRelearnStates.WaitingForPcm;
    private object sync = new object();
    private bool startClicked = false;

    public CrankRelearnModel(
        INavigator navigator,
        IConnectionService connectionService,
        DispatcherQueue dispatcherQueue,
        LoggerAdapter progressLogger) : this()
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        this.progressLogger = progressLogger ?? throw new ArgumentNullException(nameof(progressLogger));
        this.dispatcherQueue = dispatcherQueue;
        this.Enqueue();
    }

    public void StopTimer()
    {
        cancellation.Cancel();
    }

    public string Title => "Crank Relearn";

    public IState<string> CoolantTemperature => State<string>.Value(this, () => unavailable);
    public IState<string> CoolantTemperatureStatus => State<string>.Value(this, () => unavailable);
    public IState<string> Rpm => State<string>.Value(this, () => unavailable);
    public IState<string> RpmStatus => State<string>.Value(this, () => unavailable);
    public IState<string> BrakePedal => State<string>.Value(this, () => unavailable);
    public IState<string> BrakePedalStatus => State<string>.Value(this, () => unavailable);
    public IState<string> AirConditioning => State<string>.Value(this, () => unavailable);
    public IState<string> AirConditioningStatus => State<string>.Value(this, () => unavailable);
    public IState<string> Status => State<string>.Value(this, () => unavailable);
    public IState<string> Instructions => State<string>.Value(this, () => unavailable);
    
    public IState<bool> StartEnabled => State<bool>.Value(this, () => false);

    private void Enqueue()
    {
        this.dispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(100); 
            await this.MainLoop();
        });
    }

    private async Task MainLoop()
    {
        try
        {
            using (ConnectionLease lease = await this.connectionService.BeginActivity("Crank Relearn", true))
            using (new AwayMode())
            {
                Vehicle vehicle = lease.Vehicle;
                bool done = false;
                while (!done)
                {
                    int startTime = Environment.TickCount;
                    bool conditionsMet;
                    switch (this.state)
                    {
                        case CrankRelearnStates.WaitingForPcm:
                            conditionsMet = await this.CheckConditions(vehicle);
                            await UpdateUI(conditionsMet);
                            if (conditionsMet)
                            {
                                this.state = CrankRelearnStates.WaitingForUser;
                            }
                            break;

                        case CrankRelearnStates.WaitingForUser:
                            conditionsMet = await this.CheckConditions(vehicle);
                            await UpdateUI(conditionsMet);

                            if (!conditionsMet)
                            {
                                this.state = CrankRelearnStates.WaitingForPcm;
                                continue;
                            }

                            if (this.startClicked)
                            {
                                this.startClicked = false;
                                this.state = CrankRelearnStates.AllConditionsMet;
                                await vehicle.BeginCrankRelearn();
                                this.state = CrankRelearnStates.RevUp;
                                await this.UpdateUI(true);
                            }
                            break;


                        case CrankRelearnStates.RevUp:
                        case CrankRelearnStates.RevDown:
                            await this.MonitorProgress(vehicle);
                            await this.UpdateUI(true);
                            break;

                        case CrankRelearnStates.Success:
                        case CrankRelearnStates.Failure:
                            await this.UpdateUI(false);
                            done = true;
                            break;
                    }

                    if (this.cancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    int elapsedTime = Environment.TickCount - startTime;
                    int delay = Math.Max(1000 - elapsedTime, 100);
                    await Task.Delay(delay);
                }
            }
        }
        catch (Exception ex)
        {
            await this.Status.SetAsync("Something ain't right." + ex.Message);
            await this.Instructions.SetAsync("Please wait a moment, then try again.");
            this.progressLogger.AddDebugMessage("CrankRelearnMode.StateMachine: " + ex.ToString());
        }

        // Let the last message stay there for a while, then try again.
        if (!this.cancellation.IsCancellationRequested)
        {
            this.dispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(1500);
                await this.MainLoop();
            });
        }
    }

    [Command]
    public void StartClicked()
    {
        this.startClicked = true;
    }

    public async Task MonitorProgress(Vehicle vehicle)
    {
        var learnStateResponse = await vehicle.GetPid(0x12F0);
        if (learnStateResponse.Status != ResponseStatus.Success)
        {
            this.state = CrankRelearnStates.Failure;
            return;
        }

        int value = learnStateResponse.Value;
        switch (value)
        {
            case 0x00:
                // PCM is waiting for RPM to increase to threshold.
                break;

            case 0x40:
                this.state = CrankRelearnStates.RevDown;
                
                break;

            case 0x80:
                this.state = CrankRelearnStates.Success;
                await this.UpdateUI(true);
                break;

            default:
                this.state = CrankRelearnStates.Failure;
                await this.UpdateUI(true);
                break;
        }
    }

    private async Task UpdateUI(bool conditionsMet)
    {
        switch (this.state)
        {
            case CrankRelearnStates.WaitingForPcm:
                if (conditionsMet)
                {
                    await this.StartEnabled.SetAsync(true);
                    await this.Status.SetAsync("Ready to learn.");
                    await this.Instructions.SetAsync("Press the start button to begin.");
                }
                else
                {
                    await this.StartEnabled.SetAsync(false);
                    await this.Status.SetAsync("Waiting for conditions to be met.");
                    await this.Instructions.SetAsync("Ensure that all conditions are met.");
                }
                break;

            case CrankRelearnStates.RevUp:
                await this.Status.SetAsync("Preparing...");
                await this.Instructions.SetAsync("Increase RPM to 5000, or until RPM limiter engages.");
                break;

            case CrankRelearnStates.RevDown:
                await this.Status.SetAsync("Learning...");
                await this.Instructions.SetAsync("Release the throttle and wait for RPM to return to idle.");
                break;

            case CrankRelearnStates.Success:
                await this.Status.SetAsync("Success!");
                await this.Instructions.SetAsync("You're done, it worked.");
                break;

            case CrankRelearnStates.Failure:
                await this.Status.SetAsync("Learning failed.");
                await this.Instructions.SetAsync("Try again?");
                break;
        }
    }

    private async Task<bool> CheckConditions(Vehicle vehicle)
    {
        var coolantResponse = await vehicle.GetPid(0x0005);
        if (coolantResponse.Status == ResponseStatus.Success)
        {
            int value = coolantResponse.Value;
            await this.CoolantTemperature.SetAsync($"{value} °C");

            if (value > 80)
            {
                await this.CoolantTemperatureStatus.SetAsync(ready);
            }
            else
            {
                await this.CoolantTemperatureStatus.SetAsync(notReady);
            }
        }
        else
        {
            await this.CoolantTemperature.SetAsync(unavailable);
            await this.CoolantTemperatureStatus.SetAsync(notReady);
        }

        var rpmResponse = await vehicle.GetPid(0x000C);
        if (rpmResponse.Status == ResponseStatus.Success)
        {
            int value = rpmResponse.Value / 4;
            await this.Rpm.SetAsync($"{value} RPM");

            if (value < 1100)
            {
                await this.RpmStatus.SetAsync(ready);
            }
            else
            {
                await this.RpmStatus.SetAsync(notReady);
            }
        }
        else
        {
            await this.Rpm.SetAsync(unavailable);
            await this.RpmStatus.SetAsync(notReady);
        }

        var brakeResponse = await vehicle.GetPid(0x1102);
        if (brakeResponse.Status == ResponseStatus.Success)
        {
            bool value = (brakeResponse.Value & 8) > 0;
            await this.BrakePedal.SetAsync(value ? "Pressed" : "Not Pressed");

            if (value)
            {
                await this.BrakePedalStatus.SetAsync(ready);
            }
            else
            {
                await this.BrakePedalStatus.SetAsync(notReady);
            }
        }
        else
        {
            await this.BrakePedal.SetAsync(unavailable);
            await this.BrakePedalStatus.SetAsync(notReady);
        }

        var airConditioningResponse = await vehicle.GetPid(0x1100);
        if (airConditioningResponse.Status == ResponseStatus.Success)
        {
            // This has not been confirmed yet...
            bool value = (airConditioningResponse.Value & 1) > 0;
            await this.AirConditioning.SetAsync(value ? "On" : "Off");

            if (!value)
            {
                await this.AirConditioningStatus.SetAsync(ready);
            }
            else
            {
                await this.AirConditioningStatus.SetAsync(notReady);
            }
        }
        else
        {
            await this.AirConditioning.SetAsync(unavailable);
            await this.AirConditioningStatus.SetAsync(notReady);
        }

        bool conditionsMet = await this.CoolantTemperatureStatus.Value() == ready &&
           await this.RpmStatus.Value() == ready &&
           await this.BrakePedalStatus.Value() == ready &&
           await this.AirConditioningStatus.Value() == ready;
        return conditionsMet;
    }
}
