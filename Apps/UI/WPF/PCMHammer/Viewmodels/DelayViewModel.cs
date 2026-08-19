using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMHammer.Helpers;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCMHammer.Viewmodels
{
    public partial class DelayViewModel : ObservableObject
    {
        // Properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TimerText))]
        public partial int TimerValue { get; set; } = 10;

        [ObservableProperty]
        public partial string TimerText { get; set; } = "10 seconds remaining...";

        // Events
        public event Action? RequestClose;

        // Commands
        [RelayCommand]
        public void CloseCommand()
        {
            _waitTimer.Stop();
            RequestClose?.Invoke();
        }

        private readonly DispatcherTimer _waitTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        public DelayViewModel()
        {
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
    }
}
