using PcmHacking;
using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Helpers
{
    public class MainWindowLogger(MainWindowViewModel viewModel) : ILogger
    {
        private readonly MainWindowViewModel _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // Events
        public event Action<double, bool>? ProgressBarUpdated;
        public event Action<string>? StatusTextUpdated;

        // Append User messages directly to the UI Log Text
        public void AddUserMessage(string message)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";

            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.LogText += formatted + Environment.NewLine;
            });
        }

        // Append Debug messages
        public void AddDebugMessage(string message)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.DebugLogText += formatted + Environment.NewLine;
            });
        }

        // Keep the Status indicators seamlessly in sync!
        public void StatusUpdateActivity(string activity)
        {
            Application.Current.Dispatcher.Invoke(() => _viewModel.StatusText = activity);
        }

        public void StatusUpdateTimeRemaining(string remaining)
        {
            Application.Current.Dispatcher.Invoke(() => _viewModel.TimeRemaining = remaining);
        }

        public void StatusUpdatePercentDone(string percent) => StatusTextUpdated?.Invoke(percent);

        public void StatusUpdateProgressBar(double percent, bool visible) => ProgressBarUpdated?.Invoke(percent, visible);

        public void StatusUpdateRetryCount(string retries)
        {
            if (int.TryParse(retries, out int result))
                Application.Current.Dispatcher.Invoke(() => _viewModel.RetryCount = result);
        }

        public void StatusUpdateKbps(string Kbps)
        {
            // The library sends the rate as e.g. "45.23 Kbps" (capital K), or an empty string to
            // clear it. Take the leading numeric token so the unit's text/case can't defeat the parse
            // (the old code only stripped a lowercase "kbps", so nothing ever parsed and the rate
            // stayed at zero, hence invisible).
            string number = (Kbps ?? string.Empty).Trim().Split(' ')[0];
            if (double.TryParse(number, out double result))
                Application.Current.Dispatcher.Invoke(() => _viewModel.TransferRate = result);
            else if (string.IsNullOrWhiteSpace(Kbps))
                Application.Current.Dispatcher.Invoke(() => _viewModel.TransferRate = 0);
        }

        public void StatusUpdateReset()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.StatusText = "Ready";
                _viewModel.ProgressPercent = 0;
                _viewModel.RetryCount = 0;
                _viewModel.TransferRate = 0;
                _viewModel.TimeRemaining = string.Empty;
            });
        }
    }
}