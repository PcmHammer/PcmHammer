using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public class SpeedOption
    {
        public required string DisplayText { get; set; }
        public int Value { get; set; }
    }
    public partial class BruteForceViewModel : ViewModelBase
    {
        // --- Properties ---
        private readonly Vehicle _vehicle;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        public ObservableCollection<SpeedOption> SpeedOptions { get; } = [];

        // --- Bindable Properties ---
        private int _startKey = 0x0000;
        public int StartKey
        {
            get => _startKey;
            set { if (SetProperty(ref _startKey, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private int _endKey = 0xFFFF;
        public int EndKey
        {
            get => _endKey;
            set { if (SetProperty(ref _endKey, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private int _currentKey = 0x0000;
        public int CurrentKey
        {
            get => _currentKey;
            set => SetProperty(ref _currentKey, value);
        }

        private bool _algoSweepFirst = true;
        public bool AlgoSweepFirst
        {
            get => _algoSweepFirst;
            set => SetProperty(ref _algoSweepFirst, value);
        }

        private int _bruteForceSpeed = 0;
        public int BruteForceSpeed
        {
            get => _bruteForceSpeed;
            set => SetProperty(ref _bruteForceSpeed, value);
        }

        private double _progressValue = 0.0;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private double _lockoutProgress = 0.0;
        public double LockoutProgress
        {
            get => _lockoutProgress;
            set => SetProperty(ref _lockoutProgress, value);
        }

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _bruteForceRunning = false;
        public bool BruteForceRunning
        {
            get => _bruteForceRunning;
            private set
            {
                if (SetProperty(ref _bruteForceRunning, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        // --- Events ---
        public event Action? RequestClose;

        // --- Commands ---
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ExitCommand { get; }

        public BruteForceViewModel(Vehicle vehicle, ILogger logger)
        {
            _vehicle = vehicle;
            _logger = logger;

            StartCommand = new RelayCommand(async () => await StartBruteForce(), () => !BruteForceRunning && StartKey <= EndKey);
            StopCommand = new RelayCommand(StopBruteForce, () => BruteForceRunning);
            ExitCommand = new RelayCommand(() => RequestClose?.Invoke());

            PopulateSpeedOptions();
        }

        private void PopulateSpeedOptions()
        {
            SpeedOptions.Add(new SpeedOption { DisplayText = "Auto", Value = 0 });
            for (int i = 1; i <= BruteForcer.MaxSecurityDelaySeconds; i++)
                SpeedOptions.Add(new SpeedOption { DisplayText = $"{i}s", Value = i });
        }

        private async Task StartBruteForce()
        {
            BruteForceRunning = true;
            _logger.AddUserMessage("Brute force: Start.");
            StatusText = "Starting...";

            _cts = new CancellationTokenSource();
            var progress = new Progress<BruteForceProgress>(OnProgress);
            var bruteForcer = new BruteForcer(_vehicle, _logger, progress);

            int delay = BruteForceSpeed == 0 ? BruteForcer.DefaultSecurityDelaySeconds : BruteForceSpeed;

            try
            {
                using (new AwayMode())
                {
                    BruteForceResult result = await Task.Run(() =>
                        bruteForcer.BruteForce(StartKey, EndKey, AlgoSweepFirst, delay, _cts.Token));

                    HandleFinishedResult(result);
                }
            }
            catch (Exception ex)
            {
                _logger.AddUserMessage("Brute force failed: " + ex.Message);
                StatusText = "Error: " + ex.Message;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                BruteForceRunning = false;
            }
        }

        private void StopBruteForce()
        {
            _logger.AddUserMessage("Brute force: Stop.");
            StatusText = "Stopping...";
            _cts?.Cancel();
        }

        private void OnProgress(BruteForceProgress bruteForceProgress)
        {
            CurrentKey = bruteForceProgress.Key;
            ProgressValue = bruteForceProgress.Fraction * 100; // WPF ProgressBar defaults to 0-100

            string phaseStr = bruteForceProgress.Phase == BruteForcePhase.Sweeping ? "Sweeping" : "Trying";
            StatusText = $"{phaseStr} {bruteForceProgress.Key:X4}." + (string.IsNullOrEmpty(bruteForceProgress.Eta) ? "" : $" Max wait: {bruteForceProgress.Eta}");

            if (bruteForceProgress.WaitSeconds > 0)
                TriggerLockoutCountdown(bruteForceProgress.WaitSeconds);
        }

        private void TriggerLockoutCountdown(double seconds) => LockoutProgress = seconds;

        private void HandleFinishedResult(BruteForceResult result)
        {
            switch (result.Outcome)
            {
                case BruteForceOutcome.Found:
                    StatusText = result.Algorithm >= 0 ? $"Key found! {result.Key:X4} (Algo {result.Algorithm})" : $"Key found! {result.Key:X4}";
                    CurrentKey = result.Key;
                    ProgressValue = 100;
                    break;
                case BruteForceOutcome.Exhausted: StatusText = "Key not found"; break;
                case BruteForceOutcome.AlreadyUnlocked: StatusText = "The PCM is already unlocked."; break;
                case BruteForceOutcome.UnlockNotRequired: StatusText = "No unlock required (seed 0x0000)."; break;
                case BruteForceOutcome.Canceled: StatusText = "Stopped."; break;
                default: StatusText = "Stopped (communication error)."; break;
            }
            LockoutProgress = 0;
        }
    }
}
