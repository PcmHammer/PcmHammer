// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Text;
using System.Windows.Threading;

namespace PCMHammer.Helpers;

/// <summary>
/// Batches log lines from a background thread onto the UI thread, as MainWindowLogger does for the
/// Results and Debug logs. A busy 500k bus emits hundreds of frames a second, so one
/// PropertyChanged per frame would swamp the dispatcher.
/// </summary>
public sealed class LogTextBuffer : IDisposable
{
    private const int DefaultMaxLength = 500_000;

    private readonly ConcurrentQueue<string> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly Action<string> _publish;
    private readonly StringBuilder _builder = new();
    private readonly int _maxLength;

    private string _text = string.Empty;
    private bool _isDisposed;

    public LogTextBuffer(Action<string> publish, int maxLength = DefaultMaxLength)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _maxLength = maxLength;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _flushTimer.Tick += (s, e) => Flush();
    }

    public string Text => _text;

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

        _text = string.Empty;
        _publish(_text);
    }

    /// <summary>UI thread. Call after the producer stops so the last lines aren't stranded.</summary>
    public void Flush()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        _builder.Clear();
        while (_pending.TryDequeue(out string? line))
        {
            _builder.AppendLine(line);
        }

        _text = TrimToCleanLineBoundary(_text + _builder.ToString());
        _publish(_text);
    }

    /// <summary>Drops the oldest half, cutting at a newline so no line is left truncated.</summary>
    private string TrimToCleanLineBoundary(string text)
    {
        if (text.Length <= _maxLength)
        {
            return text;
        }

        int cutIndex = text.Length - (_maxLength / 2);
        int nextNewLine = text.IndexOf('\n', cutIndex);

        return nextNewLine != -1 && nextNewLine < text.Length - 1
            ? text.Substring(nextNewLine + 1)
            : text.Substring(cutIndex);
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
