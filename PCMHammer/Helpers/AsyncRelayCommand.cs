using System.Windows.Input;

namespace PCMHammer.Helpers
{
    public class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
    {
        private readonly Func<Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private readonly Func<bool>? _canExecute = canExecute;
        private bool _isExecuting;

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute == null || _canExecute());

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
