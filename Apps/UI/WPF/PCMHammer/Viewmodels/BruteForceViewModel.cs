using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace PCMHammer.Viewmodels
{
    public class SpeedOption
    {
        public required string DisplayText { get; set; }
        public int Value { get; set; }
    }

    public partial class BruteForceViewModel : ObservableObject
    {
        #region Fields
        private readonly Vehicle _vehicle;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        public ObservableCollection<SpeedOption> SpeedOptions { get; } = []; 
        #endregion

        #region Properties
        [ObservableProperty]
        public partial int StartKey { get; set; } = 0x0000;

        [ObservableProperty]
        public partial int EndKey { get; set; } = 0xFFFF;

        [ObservableProperty]
        public partial int CurrentKey { get; set; } = 0x0000;

        [ObservableProperty]
        public partial bool AlgoSweepFirst { get; set; } = true;

        [ObservableProperty]
        public partial int BruteForceSpeed { get; set; } = 0;

        [ObservableProperty]
        public partial double ProgressValue { get; set; } = 0.0;

        [ObservableProperty]
        public partial double LockoutProgress { get; set; } = 0.0;

        [ObservableProperty]
        public partial string StatusText { get; set; } = "Ready.";

        [ObservableProperty]
        public partial bool BruteForceRunning { get; set; } = false;
        #endregion

        #region Commands
        [RelayCommand(CanExecute = nameof(CanStartBruteForce))]
        private async Task StartBruteForceAsync()
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

        private bool CanStartBruteForce() => !BruteForceRunning && StartKey <= EndKey;

        [RelayCommand(CanExecute = nameof(CanStopBruteForce))]
        private void StopBruteForce()
        {
            _logger.AddUserMessage("Brute force: Stop.");
            StatusText = "Stopping...";
            _cts?.Cancel();
        }
        private bool CanStopBruteForce() => BruteForceRunning;

        [RelayCommand]
        private void Exit() => RequestClose?.Invoke();
        #endregion

        public event Action? RequestClose;

        public BruteForceViewModel(Vehicle vehicle, ILogger logger)
        {
            _vehicle = vehicle;
            _logger = logger;

            PopulateSpeedOptions();
        }

        private void PopulateSpeedOptions()
        {
            SpeedOptions.Add(new SpeedOption { DisplayText = "Auto", Value = 0 });
            for (int i = 1; i <= BruteForcer.MaxSecurityDelaySeconds; i++)
                SpeedOptions.Add(new SpeedOption { DisplayText = $"{i}s", Value = i });
        }

        private void OnProgress(BruteForceProgress bruteForceProgress)
        {
            CurrentKey = bruteForceProgress.Key;
            ProgressValue = bruteForceProgress.Fraction * 100;

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