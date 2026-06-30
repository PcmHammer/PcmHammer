using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace PCMHammer.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event for a given property name.
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) 
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Checks if a property already matches a desired value. Sets the property and
        /// raises the PropertyChanged event only when the value has actually changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">Reference to a field with the current value.</param>
        /// <param name="value">The desired value for the property.</param>
        /// <param name="propertyName">Name of the property used to notify listeners.</param>
        /// <returns>True if the value was changed, false if the existing value matched.</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Safely marshals an action back to the UI thread.
        /// Essential for updating properties from background worker tasks/threads.
        /// </summary>
        protected static void InvokeOnUIThread(Action action)
        {
            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            if (dispatcher.CheckAccess()) 
                action();
            else
                dispatcher.BeginInvoke(action);
        }
    }
}