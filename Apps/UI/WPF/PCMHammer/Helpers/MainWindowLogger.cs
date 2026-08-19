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
            if (double.TryParse(Kbps.Replace(" Kb/s", "").Replace("kbps", ""), out double result))
                Application.Current.Dispatcher.Invoke(() => _viewModel.TransferRate = result);
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