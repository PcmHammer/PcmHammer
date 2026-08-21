using PcmHacking;
using PCMHammer.Viewmodels;
using System.Collections.Concurrent;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PCMHammer.Helpers;

public class MainWindowLogger : ILogger, IDisposable
{
    private readonly MainWindowViewModel _viewModel;
    
    // Thread-safe queues for log messages
    private readonly ConcurrentQueue<string> _userMessageQueue = new();
    private readonly ConcurrentQueue<string> _debugMessageQueue = new();

    // UI Batching Timer (10 FPS)
    private readonly DispatcherTimer _flushTimer;
    private readonly StringBuilder _userStringBuilder = new();
    private readonly StringBuilder _debugStringBuilder = new();

    private const int MaxLogLength = 500_000;
    private bool _isDisposed;

    public MainWindowLogger(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _flushTimer.Tick += (s, e) => FlushQueuesToViewModel();
        _flushTimer.Start();
    }

    // Events
    public event Action<double, bool>? ProgressBarUpdated;
    public event Action<string>? StatusTextUpdated;

    // Fast, non-blocking enqueue
    public void AddUserMessage(string message)
    {
        _userMessageQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void AddDebugMessage(string message)
    {
        _debugMessageQueue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    /// <summary>
    /// Force any queued messages into the view model now instead of waiting for the next timer tick.
    /// Must be called on the UI thread. Used before the logs are saved on shutdown so the saved files
    /// include the most recent lines (they are read from the view model's LogText/DebugLogText).
    /// </summary>
    public void Flush() => FlushQueuesToViewModel();

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
        Application.Current.Dispatcher.BeginInvoke(() => _viewModel.StatusText = activity);
    }

    public void StatusUpdateTimeRemaining(string remaining)
    {
        Application.Current.Dispatcher.BeginInvoke(() => _viewModel.TimeRemaining = remaining);
    }

    // Fixed: Marshal event invocations to the UI thread to prevent cross-thread UI exceptions
    public void StatusUpdatePercentDone(string percent)
    {
        Application.Current.Dispatcher.BeginInvoke(() => StatusTextUpdated?.Invoke(percent));
    }

    public void StatusUpdateProgressBar(double percent, bool visible)
    {
        Application.Current.Dispatcher.BeginInvoke(() => ProgressBarUpdated?.Invoke(percent, visible));
    }

    public void StatusUpdateRetryCount(string retries)
    {
        if (int.TryParse(retries, out int result))
            Application.Current.Dispatcher.BeginInvoke(() => _viewModel.RetryCount = result);
    }

    public void StatusUpdateKbps(string Kbps)
    {
        // The library sends the rate as e.g. "45.23 Kbps" (capital K), or an empty string to clear it.
        // Take the leading numeric token so the unit's text/case can't defeat the parse - a plain
        // Replace("kbps", ...) misses the capital-K form, so nothing parses and the rate stays at zero
        // (invisible in the status bar).
        string number = (Kbps ?? string.Empty).Trim().Split(' ')[0];
        if (double.TryParse(number, out double result))
            Application.Current.Dispatcher.BeginInvoke(() => _viewModel.TransferRate = result);
        else if (string.IsNullOrWhiteSpace(Kbps))
            Application.Current.Dispatcher.BeginInvoke(() => _viewModel.TransferRate = 0);
    }

    public void StatusUpdateReset()
    {
        // Use synchronous Invoke here so reset state is guaranteed immediately for caller
        Application.Current.Dispatcher.Invoke(() =>
        {
            _viewModel.StatusText = "Ready";
            _viewModel.ProgressPercent = 0;
            _viewModel.RetryCount = 0;
            _viewModel.TransferRate = 0;
            _viewModel.TimeRemaining = string.Empty;
        });
    }

    // Fixed: Dispose now executes a final synchronous flush on the UI thread to prevent data loss on shutdown
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _flushTimer.Stop();

        // Perform final flush on Dispatcher to catch remaining messages
        if (Application.Current != null && Application.Current.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(FlushQueuesToViewModel);
        }
        else
        {
            FlushQueuesToViewModel();
        }
    }
}
