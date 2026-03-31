using PcmHacking.UnoUI.Services;
using PcmHacking.UnoUI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public record DelayResult(bool Proceed);

public partial record DelayModel : IDisposable
{
    // This is hacky, but see notes in WriteModel.cs.
    public static DelayResult? Result;

// Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
// That's solved in the constructor below, but Uno's code generator also created a default constructor.
#pragma warning disable CS8618
    private readonly INavigator navigator;
#pragma warning restore CS8618

    private Timer? timer;
    private int secondsRemaining = 10;
    private bool exited = false;

    public IState<string> Countdown => State<string>.Value(this, () => "10 seconds remaining.");

    public DelayModel(INavigator navigator)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.timer = new Timer(TimerCallback, null, 1000, 1000);
    }
    
    ~DelayModel()
    {
        this.Dispose(false);
    }

    public void Dispose()
    {
        this.Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.timer?.Dispose();
            this.timer = null;
        }
    }

    private async void TimerCallback(object? state)
    {
        this.secondsRemaining--;
        await this.Countdown.SetAsync(secondsRemaining + " seconds remaining.");

        if (this.secondsRemaining == 0)
        {
            if (this.Done())
            {
                return;
            }

            Result = new DelayResult(true);
            await this.navigator.NavigateBackWithResultAsync(this, data: Result);
        }
    }

    [Command]
    public async ValueTask ContinueClicked()
    {
        if (this.Done())
        {
            return;
        }

        Result = new DelayResult(true);
        await this.navigator.NavigateBackWithResultAsync(this, data: Result);
    }

    [Command]
    public async ValueTask AbortClicked()
    {
        if (this.Done())
        {
            return;
        }

        Result = new DelayResult(false);
        await this.navigator.NavigateBackWithResultAsync(this, data: Result);
    }

    /// <summary>
    /// This is used to ensure that we don't call NavigateBack twice.
    /// </summary>
    /// <returns>'false' the first time it is called, and 'true' every subsequent time.</returns>
    bool Done()
    {
        lock(this)
        {
            if (this.exited)
            {
                return true;
            }
            else
            {
                this.exited = true;
                return false;
            }
        }
    }
}