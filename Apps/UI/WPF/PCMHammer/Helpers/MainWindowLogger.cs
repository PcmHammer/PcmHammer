using PcmHacking;
using PCMHammer.Viewmodels;
using System;
using System.Windows;

namespace PCMHammer.Helpers
{
    public class MainWindowLogger(MainWindowViewModel viewModel) : ILogger
    {
        private readonly MainWindowViewModel _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // 1. Append User messages directly to the UI Log Text
        public void AddUserMessage(string message)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";

            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.LogText += formatted + Environment.NewLine;
            });
        }

        // 2. Append Debug messages (you can decide to prefix them or put them in the same box)
        public void AddDebugMessage(string message)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.DebugLogText += formatted + Environment.NewLine;
            });
        }

        // 3. Keep the Status indicators seamlessly in sync!
        public void StatusUpdateActivity(string activity)
        {
            Application.Current.Dispatcher.Invoke(() => _viewModel.StatusText = activity);
        }

        public void StatusUpdateTimeRemaining(string remaining)
        {
            Application.Current.Dispatcher.Invoke(() => _viewModel.TimeRemaining = remaining);
        }

        public void StatusUpdatePercentDone(string percent)
        {
            // Parses string percent (e.g., "45%") or sets fallback
            if (double.TryParse(percent.Replace("%", ""), out double result))
            {
                Application.Current.Dispatcher.Invoke(() => _viewModel.ProgressPercent = result);
            }
        }

        public void StatusUpdateRetryCount(string retries)
        {
            if (int.TryParse(retries, out int result))
            {
                Application.Current.Dispatcher.Invoke(() => _viewModel.RetryCount = result);
            }
        }

        public void StatusUpdateProgressBar(double completed, bool visible)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.ProgressPercent = completed;
                // If you have a Visibility property on your viewmodel, set it here
            });
        }

        public void StatusUpdateKbps(string Kbps)
        {
            if (double.TryParse(Kbps.Replace(" Kb/s", "").Replace("kbps", ""), out double result))
            {
                Application.Current.Dispatcher.Invoke(() => _viewModel.TransferRate = result);
            }
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