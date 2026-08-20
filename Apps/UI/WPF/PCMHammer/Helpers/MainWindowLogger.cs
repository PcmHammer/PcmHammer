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
        if (double.TryParse(Kbps.Replace(" Kb/s", "").Replace("kbps", ""), out double result))
            Application.Current.Dispatcher.BeginInvoke(() => _viewModel.TransferRate = result);
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
