using PcmHacking;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Helpers;

public class MainWindowLogger : ILogger, IDisposable
{
    private readonly MainWindowViewModel _viewModel;

    // The Results and Debug panes batch exactly as the Bus Monitor pane does; one primitive serves
    // all three, as LogListView does in the WinForms app.
    private readonly LogLinesSource _resultsLog = new();
    private readonly LogLinesSource _debugLog = new();

    private bool _isDisposed;

    public MainWindowLogger(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // Unlike the Bus Monitor, these two collect from startup.
        _resultsLog.Start();
        _debugLog.Start();
    }

    // Events
    public event Action<double, bool>? ProgressBarUpdated;
    public event Action<string>? StatusTextUpdated;

    /// <summary>Bound by the Results pane and read by the log save commands.</summary>
    public LogLinesSource ResultsLog => _resultsLog;

    /// <summary>Bound by the Debug pane and read by the log save commands.</summary>
    public LogLinesSource DebugLog => _debugLog;

    // Fast, non-blocking enqueue
    public void AddUserMessage(string message)
    {
        _resultsLog.Append($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void AddDebugMessage(string message)
    {
        _debugLog.Append($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    /// <summary>
    /// Force any queued messages into the buffers now instead of waiting for the next timer tick.
    /// Must be called on the UI thread. Used before the logs are saved on shutdown so the saved
    /// files include the most recent lines.
    /// </summary>
    public void Flush()
    {
        _resultsLog.Flush();
        _debugLog.Flush();
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

    // Dispose executes a final synchronous flush on the UI thread to prevent data loss on shutdown
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (Application.Current != null && Application.Current.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(Flush);
        }
        else
        {
            Flush();
        }

        _resultsLog.Dispose();
        _debugLog.Dispose();
    }
}
