using System;
using PcmHacking;

namespace PCMLoggerAvalonia.Services;

/// <summary>
/// Minimal ILogger that forwards the library's user and debug messages to callbacks. The view model
/// uses it to surface status; the status-strip updates are not shown in this first cut.
/// </summary>
public sealed class StatusLogger : ILogger
{
    private readonly Action<string> onUserMessage;
    private readonly Action<string> onDebugMessage;

    public StatusLogger(Action<string> onUserMessage, Action<string> onDebugMessage)
    {
        this.onUserMessage = onUserMessage;
        this.onDebugMessage = onDebugMessage;
    }

    public void AddUserMessage(string message) => this.onUserMessage(message);

    public void AddDebugMessage(string message) => this.onDebugMessage(message);

    public void StatusUpdateActivity(string activity) { }
    public void StatusUpdateTimeRemaining(string remaining) { }
    public void StatusUpdatePercentDone(string percent) { }
    public void StatusUpdateRetryCount(string retries) { }
    public void StatusUpdateProgressBar(double completed, bool visible) { }
    public void StatusUpdateKbps(string Kbps) { }
    public void StatusUpdateReset() { }
}
