// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Text;
using System.Windows.Threading;

namespace PCMHammer.Helpers;

/// <summary>
/// Batches log lines from a background thread onto the UI thread. A busy 500k bus emits hundreds of
/// frames a second, so one notification per frame would swamp the dispatcher.
/// <para>
/// Publishes deltas rather than the whole log. Re-publishing the full text put a half-megabyte
/// allocation on the large object heap ten times a second; the full string is now built only on
/// demand (<see cref="Snapshot"/>) and on a trim, which invalidates what the view already holds.
/// </para>
/// <para>Backs the Results, Debug and Bus Monitor panes, as LogListView does in the WinForms app.</para>
/// </summary>
public sealed class LogTextBuffer : IDisposable
{
    private const int DefaultMaxLength = 500_000;

    private readonly ConcurrentQueue<string> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly StringBuilder _log = new();
    private readonly StringBuilder _delta = new();
    private readonly int _maxLength;

    private bool _isDisposed;

    public LogTextBuffer(int maxLength = DefaultMaxLength)
    {
        _maxLength = maxLength;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _flushTimer.Tick += (s, e) => Flush();
    }

    /// <summary>UI thread. Text to add to the end of what the view already shows.</summary>
    public event Action<string>? Appended;

    /// <summary>UI thread. Cleared or trimmed, so the view must replace everything it holds.</summary>
    public event Action<string>? Replaced;

    /// <summary>UI thread. Allocates the whole log; for saving and for attaching a view.</summary>
    public string Snapshot() => _log.ToString();

    /// <summary>UI thread. Only runs while a producer is active; the tab is usually never opened.</summary>
    public void Start() => _flushTimer.Start();

    /// <summary>UI thread. Flush() first to publish what is still queued.</summary>
    public void Stop() => _flushTimer.Stop();

    /// <summary>Any thread.</summary>
    public void Append(string line) => _pending.Enqueue(line);

    /// <summary>UI thread.</summary>
    public void Clear()
    {
        while (_pending.TryDequeue(out _))
        {
        }

        _log.Clear();
        Replaced?.Invoke(string.Empty);
    }

    /// <summary>UI thread. Call after the producer stops so the last lines aren't stranded.</summary>
    public void Flush()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        _delta.Clear();
        while (_pending.TryDequeue(out string? line))
        {
            _delta.AppendLine(line);
        }

        _log.Append(_delta);

        if (_log.Length > _maxLength)
        {
            Replaced?.Invoke(Trim());
        }
        else
        {
            Appended?.Invoke(_delta.ToString());
        }
    }

    /// <summary>Drops the oldest half, cutting at a newline so no line is left truncated.</summary>
    private string Trim()
    {
        string text = _log.ToString();
        int cutIndex = text.Length - (_maxLength / 2);
        int nextNewLine = text.IndexOf('\n', cutIndex);

        string kept = nextNewLine != -1 && nextNewLine < text.Length - 1
            ? text.Substring(nextNewLine + 1)
            : text.Substring(cutIndex);

        _log.Clear();
        _log.Append(kept);
        return kept;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _flushTimer.Stop();
    }
}
