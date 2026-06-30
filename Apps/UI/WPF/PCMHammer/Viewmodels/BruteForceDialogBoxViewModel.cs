using PcmHacking;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PCMHammer.Viewmodels
{
    public class SpeedOption
    {
        public string DisplayText { get; set; }
        public int Value { get; set; }
    }
    public partial class BruteForceDialogBoxViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<SpeedOption> SpeedOptions { get; set; } = [];

        // Properties
        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value; 
                OnPropertyChanged(nameof(StatusText)); 
            }
        }
        
        private bool _algoSweepFirst;
        public bool AlgoSweepFirst
        {
            get => _algoSweepFirst;
            set
            {
                if (_algoSweepFirst == value) return;
                _algoSweepFirst = value;
                OnPropertyChanged(nameof(AlgoSweepFirst));
            }
        }

        private bool _bruteForceRunning;
        public bool BruteForceRunning
        {
            get => _bruteForceRunning;
            set
            {
                if (_bruteForceRunning == value) return;
                _bruteForceRunning = value;
                OnPropertyChanged(nameof(BruteForceRunning));
            }
        }

        private int _bruteForceSpeed; // 0 = auto
        public int BruteForceSpeed
        {
            get => _bruteForceSpeed;
            set
            {
                if (_bruteForceSpeed == value) return;
                _bruteForceSpeed = value;
                OnPropertyChanged(nameof(BruteForceSpeed));
            }
        }

        private string _startKey = "0000";
        public string StartKey
        {
            get => _startKey;
            set
            {
                if (_startKey == value) return;
                _startKey = value;
                OnPropertyChanged(nameof(StartKey));
            }
        }

        private string _endKey = "FFFF";
        public string EndKey
        {
            get => _endKey;
            set
            {
                if (_endKey == value) return;
                _endKey = value;
                OnPropertyChanged(nameof(EndKey));
            }
        }

        private string _currentKey = "0000";
        public string CurrentKey
        {
            get => _currentKey;
            set
            {
                if (_currentKey == value) return;
                _currentKey = value;
                OnPropertyChanged(nameof(CurrentKey));
            }
        }

        public BruteForceDialogBoxViewModel()
        {
            PopulateSpeedOptions();
        }

        private void PopulateSpeedOptions()
        {
            // Add Auto first
            SpeedOptions.Add(new SpeedOption { DisplayText = "Auto", Value = 0 });

            // Dynamically add the rest
            for (int seconds = 1; seconds <= BruteForcer.MaxSecurityDelaySeconds; seconds++)
            {
                SpeedOptions.Add(new SpeedOption
                {
                    DisplayText = $"{seconds} seconds",
                    Value = seconds
                });
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string propertyName) => 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
