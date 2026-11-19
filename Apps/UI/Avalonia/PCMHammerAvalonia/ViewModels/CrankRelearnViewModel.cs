using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PCMHammerAvalonia.ViewModels;

public enum CrankRelearnState
{
    WaitingForPcm,
    WaitingForUser,
    RevUp,
    RevDown,
    Success,
    Failure,
}

/// <summary>
/// Guides a crank sensor relearn. Ports the Uno Crank Relearn page: it polls the PCM for the entry
/// conditions (warm engine, idling, brake pressed, A/C off), sends the relearn request when the user
/// starts it, then follows the PCM's learn state until it reports success or failure.
/// </summary>
public partial class CrankRelearnViewModel : ObservableObject
{
    private const string Ready = "Ready";
    private const string NotReady = "Not Ready";
    private const string Unavailable = "---";

    private readonly Vehicle _vehicle;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private CrankRelearnState _state = CrankRelearnState.WaitingForPcm;
    private volatile bool _startClicked;

    [ObservableProperty]
    public partial string CoolantTemperature { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string CoolantTemperatureStatus { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string Rpm { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string RpmStatus { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string BrakePedal { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string BrakePedalStatus { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string AirConditioning { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string AirConditioningStatus { get; set; } = Unavailable;

    [ObservableProperty]
    public partial string Status { get; set; } = "Waiting for the PCM.";

    [ObservableProperty]
    public partial string Instructions { get; set; } = "Checking the conditions...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool StartEnabled { get; set; }

    public event Action? RequestClose;

    public CrankRelearnViewModel(Vehicle vehicle, ILogger logger)
    {
        _vehicle = vehicle;
        _logger = logger;
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    // Parameterless constructor for the XAML previewer.
    public CrankRelearnViewModel()
    {
        _vehicle = null!;
        _logger = null!;
    }

    [RelayCommand(CanExecute = nameof(StartEnabled))]
    private void Start()
    {
        _startClicked = true;
    }

    [RelayCommand]
    private void Close()
    {
        Stop();
        RequestClose?.Invoke();
    }

    /// <summary>Stops polling the PCM. Safe to call more than once.</summary>
    public void Stop() => _cts.Cancel();

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int startTime = Environment.TickCount;
            try
            {
                await StepAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.AddDebugMessage("CrankRelearn: " + exception);
                Post(() =>
                {
                    Status = "Something isn't right: " + exception.Message;
                    Instructions = "Please wait a moment, then try again.";
                    StartEnabled = false;
                });
                _state = CrankRelearnState.WaitingForPcm;
                _startClicked = false;
            }

            try
            {
                int delay = Math.Max(1000 - (Environment.TickCount - startTime), 100);
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task StepAsync()
    {
        bool conditionsMet;
        switch (_state)
        {
            case CrankRelearnState.WaitingForPcm:
                conditionsMet = await CheckConditions();
                UpdateUi(conditionsMet);
                if (conditionsMet) _state = CrankRelearnState.WaitingForUser;
                break;

            case CrankRelearnState.WaitingForUser:
                conditionsMet = await CheckConditions();
                UpdateUi(conditionsMet);
                if (!conditionsMet)
                {
                    _state = CrankRelearnState.WaitingForPcm;
                    _startClicked = false;
                    break;
                }

                if (_startClicked)
                {
                    _startClicked = false;
                    _logger.AddUserMessage("Starting crank relearn.");
                    Response<int> begin = await _vehicle.BeginCrankRelearn();
                    _state = begin.Status == ResponseStatus.Success
                        ? CrankRelearnState.RevUp
                        : CrankRelearnState.Failure;
                    UpdateUi(true);
                }
                break;

            case CrankRelearnState.RevUp:
            case CrankRelearnState.RevDown:
                await MonitorProgress();
                UpdateUi(true);
                break;

            case CrankRelearnState.Success:
            case CrankRelearnState.Failure:
                // Leave the result on screen for a moment, then start over so the user can try again.
                UpdateUi(false);
                if (_state == CrankRelearnState.Failure)
                {
                    await Task.Delay(3000);
                    _state = CrankRelearnState.WaitingForPcm;
                }
                break;
        }
    }

    private async Task MonitorProgress()
    {
        Response<int> learnState = await _vehicle.GetPid(0x12F0);
        if (learnState.Status != ResponseStatus.Success)
        {
            _state = CrankRelearnState.Failure;
            return;
        }

        switch (learnState.Value)
        {
            case 0x00:
                // The PCM is waiting for RPM to increase to its threshold.
                break;

            case 0x40:
                _state = CrankRelearnState.RevDown;
                break;

            case 0x80:
                _state = CrankRelearnState.Success;
                _logger.AddUserMessage("Crank relearn succeeded.");
                break;

            default:
                _state = CrankRelearnState.Failure;
                _logger.AddUserMessage("Crank relearn failed.");
                break;
        }
    }

    private void UpdateUi(bool conditionsMet)
    {
        CrankRelearnState state = _state;
        Post(() =>
        {
            switch (state)
            {
                case CrankRelearnState.WaitingForPcm:
                case CrankRelearnState.WaitingForUser:
                    StartEnabled = conditionsMet;
                    Status = conditionsMet ? "Ready to learn." : "Waiting for conditions to be met.";
                    Instructions = conditionsMet
                        ? "Press the Start button to begin."
                        : "Ensure that all conditions are met.";
                    break;

                case CrankRelearnState.RevUp:
                    StartEnabled = false;
                    Status = "Preparing...";
                    Instructions = "Increase RPM to 5000, or until the RPM limiter engages.";
                    break;

                case CrankRelearnState.RevDown:
                    StartEnabled = false;
                    Status = "Learning...";
                    Instructions = "Release the throttle and wait for RPM to return to idle.";
                    break;

                case CrankRelearnState.Success:
                    StartEnabled = false;
                    Status = "Success!";
                    Instructions = "You're done, it worked.";
                    break;

                case CrankRelearnState.Failure:
                    StartEnabled = false;
                    Status = "Learning failed.";
                    Instructions = "Try again.";
                    break;
            }
        });
    }

    private async Task<bool> CheckConditions()
    {
        bool coolantOk = false, rpmOk = false, brakeOk = false, acOk = false;
        string coolant = Unavailable, rpm = Unavailable, brake = Unavailable, ac = Unavailable;

        Response<int> coolantResponse = await _vehicle.GetPid(0x0005);
        if (coolantResponse.Status == ResponseStatus.Success)
        {
            coolant = $"{coolantResponse.Value} °C";
            coolantOk = coolantResponse.Value > 80;
        }

        Response<int> rpmResponse = await _vehicle.GetPid(0x000C);
        if (rpmResponse.Status == ResponseStatus.Success)
        {
            int value = rpmResponse.Value / 4;
            rpm = $"{value} RPM";
            rpmOk = value < 1100;
        }

        Response<int> brakeResponse = await _vehicle.GetPid(0x1102);
        if (brakeResponse.Status == ResponseStatus.Success)
        {
            bool pressed = (brakeResponse.Value & 8) > 0;
            brake = pressed ? "Pressed" : "Not Pressed";
            brakeOk = pressed;
        }

        Response<int> acResponse = await _vehicle.GetPid(0x1100);
        if (acResponse.Status == ResponseStatus.Success)
        {
            // This bit has not been confirmed yet (same note as the Uno version).
            bool on = (acResponse.Value & 1) > 0;
            ac = on ? "On" : "Off";
            acOk = !on;
        }

        Post(() =>
        {
            CoolantTemperature = coolant;
            CoolantTemperatureStatus = coolantOk ? Ready : NotReady;
            Rpm = rpm;
            RpmStatus = rpmOk ? Ready : NotReady;
            BrakePedal = brake;
            BrakePedalStatus = brakeOk ? Ready : NotReady;
            AirConditioning = ac;
            AirConditioningStatus = acOk ? Ready : NotReady;
        });

        return coolantOk && rpmOk && brakeOk && acOk;
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
