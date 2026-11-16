using Avalonia.Threading;
using PcmHacking;
using PCMHammerAvalonia.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace PCMHammerAvalonia.Helpers;

public class MainWindowLogger : ILogger, IDisposable
{
    private readonly MainWindowViewModel _viewModel;

    // Thread-safe queues for log messages
    private readonly ConcurrentQueue<string> _userMessageQueue = new();
    private readonly ConcurrentQueue<string> _debugMessageQueue = new();

    private readonly StringBuilder _userStringBuilder = new();
    private readonly StringBuilder _debugStringBuilder = new();

    // Coalesces flushes: many enqueues schedule at most one pending UI-thread flush.
    private int _flushScheduled;

    private const int MaxLogLength = 500_000;
    private bool _isDisposed;

    public MainWindowLogger(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    // Events
    public event Action<double, bool>? ProgressBarUpdated;
    public event Action<string>? StatusTextUpdated;

    // Fast, non-blocking enqueue
    public void AddUserMessage(string message)
    {
        _userMessageQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        ScheduleFlush();
    }

    public void AddDebugMessage(string message)
    {
        _debugMessageQueue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        ScheduleFlush();
    }

    // Flush on the UI thread. A DispatcherTimer bound at construction can be tied to a non-UI thread
    // (the mobile view-model factory does not run on the UI thread), so it may never tick; posting to
    // Dispatcher.UIThread always targets the UI thread. Coalesced so a burst of messages causes one flush.
    private void ScheduleFlush()
    {
        if (System.Threading.Interlocked.Exchange(ref _flushScheduled, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                System.Threading.Interlocked.Exchange(ref _flushScheduled, 0);
                FlushQueuesToViewModel();
            },
            DispatcherPriority.Background);
    }

    private void FlushQueuesToViewModel()
    {
        // 1. Process User Messages
        if (!_userMessageQueue.IsEmpty)
        {
            _userStringBuilder.Clear();
            while (_userMessageQueue.TryDequeue(out string? msg))
            {
                _userStringBuilder.AppendLine(msg);
            }

            string current = _viewModel.LogText ?? string.Empty;
            string updated = current + _userStringBuilder.ToString();

            _viewModel.LogText = TrimToCleanLineBoundary(updated);
        }

        // 2. Process Debug Messages
        if (!_debugMessageQueue.IsEmpty)
        {
            _debugStringBuilder.Clear();
            while (_debugMessageQueue.TryDequeue(out string? msg))
            {
                _debugStringBuilder.AppendLine(msg);
            }

            string current = _viewModel.DebugLogText ?? string.Empty;
            string updated = current + _debugStringBuilder.ToString();

            _viewModel.DebugLogText = TrimToCleanLineBoundary(updated);
        }
    }

    // Truncates log length safely at a clean newline boundary rather than mid-string
    private static string TrimToCleanLineBoundary(string text)
    {
        if (text.Length <= MaxLogLength)
            return text;

        int cutIndex = text.Length - (MaxLogLength / 2);
        int nextNewLine = text.IndexOf('\n', cutIndex);

        return nextNewLine != -1 && nextNewLine < text.Length - 1
            ? text.Substring(nextNewLine + 1)
            : text.Substring(cutIndex);
    }

    // Status Updates: Safely Dispatch to UI Thread
    public void StatusUpdateActivity(string activity)
    {
        Dispatcher.UIThread.Post(() => _viewModel.StatusText = activity);
    }

    public void StatusUpdateTimeRemaining(string remaining)
    {
        Dispatcher.UIThread.Post(() => _viewModel.TimeRemaining = remaining);
    }

    public void StatusUpdatePercentDone(string percent)
    {
        Dispatcher.UIThread.Post(() => StatusTextUpdated?.Invoke(percent));
    }

    public void StatusUpdateProgressBar(double percent, bool visible)
    {
        Dispatcher.UIThread.Post(() => ProgressBarUpdated?.Invoke(percent, visible));
    }

    public void StatusUpdateRetryCount(string retries)
    {
        if (int.TryParse(retries, out int result))
            Dispatcher.UIThread.Post(() => _viewModel.RetryCount = result);
    }

    public void StatusUpdateKbps(string Kbps)
    {
        if (double.TryParse(Kbps.Replace(" Kb/s", "").Replace("kbps", ""), out double result))
            Dispatcher.UIThread.Post(() => _viewModel.TransferRate = result);
    }

    public void StatusUpdateReset()
    {
        // Use synchronous Invoke here so reset state is guaranteed immediately for caller
        Dispatcher.UIThread.Invoke(() =>
        {
            _viewModel.StatusText = "Ready";
            _viewModel.ProgressPercent = 0;
            _viewModel.RetryCount = 0;
            _viewModel.TransferRate = 0;
            _viewModel.TimeRemaining = string.Empty;
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Perform final flush on Dispatcher to catch remaining messages
        if (Dispatcher.UIThread.CheckAccess())
        {
            FlushQueuesToViewModel();
        }
        else
        {
            Dispatcher.UIThread.Invoke(FlushQueuesToViewModel);
        }
    }
}