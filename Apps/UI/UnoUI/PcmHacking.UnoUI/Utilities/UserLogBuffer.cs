// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace PcmHacking.UnoUI.Utilities;

/// <summary>
/// Accumulates log lines and pushes them to the UI on a timer.
/// </summary>
/// <remarks>
/// The models used to re-join every message seen so far into one string on each new message, which is
/// quadratic in both work and allocation and made a long read crawl - worst on Android, where the
/// re-render is slowest. Appending to a builder and flushing periodically keeps it linear.
/// </remarks>
public sealed class UserLogBuffer : IDisposable
{
    // Android re-renders a large text block far more slowly, so it flushes less often.
#if ANDROID
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(1000);
#else
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);
#endif

    private readonly StringBuilder text = new StringBuilder();
    private readonly object gate = new object();
    private readonly Func<string, Task> publish;
    private Timer? timer;
    private bool dirty;

    public UserLogBuffer(Func<string, Task> publish)
    {
        this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
        this.timer = new Timer(_ => this.Flush(), null, FlushInterval, FlushInterval);
    }

    /// <summary>Queue a line. Safe to call from an operation's worker thread.</summary>
    public void Append(string message)
    {
        lock (this.gate)
        {
            if (this.text.Length > 0)
            {
                this.text.Append("\r\n");
            }

            this.text.Append(message);
            this.dirty = true;
        }
    }

    /// <summary>Publish immediately, so the last lines of an operation are not left pending.</summary>
    public async Task FlushNow()
    {
        string? pending = this.TakePending();
        if (pending != null)
        {
            await this.publish(pending);
        }
    }

    private async void Flush()
    {
        string? pending = this.TakePending();
        if (pending == null)
        {
            return;
        }

        try
        {
            await this.publish(pending);
        }
        catch
        {
            // A failed UI update must not take down the timer thread.
        }
    }

    private string? TakePending()
    {
        lock (this.gate)
        {
            if (!this.dirty)
            {
                return null;
            }

            this.dirty = false;
            return this.text.ToString();
        }
    }

    public void Dispose()
    {
        this.timer?.Dispose();
        this.timer = null;
    }
}
