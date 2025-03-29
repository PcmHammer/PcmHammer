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
    private readonly INavigator navigator;
    private Timer? timer;
    private int secondsRemaining = 10;
    private bool exited = false;

    public IState<string> Countdown => State<string>.Value(this, () => "10 seconds remaining.");

    public DelayModel(INavigator navigator)
    {
        this.navigator = navigator;
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

            await this.navigator.NavigateBackWithResultAsync(this, data: new DelayResult(true));
        }
    }

    [Command]
    public async ValueTask ContinueClicked()
    {
        if (this.Done())
        {
            return;
        }

        await this.navigator.NavigateBackWithResultAsync(this, data: new DelayResult(true));
    }

    [Command]
    public async ValueTask AbortClicked()
    {
        if (this.Done())
        {
            return;
        }

        await this.navigator.NavigateBackWithResultAsync(this, data: new DelayResult(false));
    }

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