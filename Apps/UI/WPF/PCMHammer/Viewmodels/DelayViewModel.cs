using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMHammer.Helpers;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCMHammer.Viewmodels
{
    public partial class DelayViewModel : ObservableObject
    {
        private const int CountdownSeconds = 10;

        // Properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TimerText))]
        public partial int TimerValue { get; set; } = CountdownSeconds;

        // Computed, not stored: as a stored [ObservableProperty] this held its initial string
        // forever, so the dialog always read "10 seconds remaining..." and never counted down.
        // NotifyPropertyChangedFor on TimerValue re-raises this, which only does something when
        // there is a getter to re-evaluate.
        public string TimerText => TimerValue == 1
            ? "1 second remaining..."
            : $"{TimerValue} seconds remaining...";

        // Events
        public event Action? RequestClose;

        // Commands. Do NOT name this method "CloseCommand": the generator appends "Command" to the
        // method name, so CloseCommand() produces CloseCommandCommand and the XAML binding to
        // CloseCommand silently resolves to nothing. See WriteTypeViewModel for the same trap.
        [RelayCommand]
        public void Close()
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

                if (TimerValue == 0)
                {
                    _waitTimer.Stop();
                    RequestClose?.Invoke();
                }
            };
            _waitTimer.Start();
        }

        /// <summary>
        /// Stops the countdown. Called when the dialog closes by any route, so a timer belonging to
        /// a dismissed dialog cannot keep ticking and raise RequestClose against a closed window.
        /// </summary>
        public void StopTimer() => _waitTimer.Stop();
    }
}
