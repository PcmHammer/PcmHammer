using PCMHammer.Helpers;
using PCMHammer.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCMHammer.Viewmodels
{
    public partial class DelayViewModel : ViewModelBase
    {
        // Properties
        private int _timerValue = 10;
        public int TimerValue
        {
            get => _timerValue;
            set
            {
                SetProperty(ref _timerValue, value);
                TimerText = $"{TimerValue} seconds remaining...";
            }
        }

        private string _timerText = "10 seconds remaining...";
        public string TimerText
        {
            get => _timerText;
            set => SetProperty(ref _timerText, value);
        }

        // Events
        public event Action? RequestClose;

        // Commands
        public ICommand CloseCommand { get; }

        private readonly DispatcherTimer _waitTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        public DelayViewModel()
        {
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            _waitTimer.Tick += (s, e) =>
            {
                if (TimerValue > 0) TimerValue--;
                else
                {
                    _waitTimer.Stop();
                    RequestClose?.Invoke();
                }
            };
            _waitTimer.Start();
        }

        // INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
