using PCMHammer.Helpers;
using PCMHammer.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class UserDefinedKeyDialogBoxViewModel : ViewModelBase
    {
        // Properties
        public string _userDefinedKey = string.Empty;
        public string UserDefinedKey
        {
            get => _userDefinedKey;
            set => SetProperty(ref _userDefinedKey, value);
        }

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptAndClose;

        // Commands
        public ICommand CancelCommand { get; }
        public ICommand AcceptCommand { get; }

        public UserDefinedKeyDialogBoxViewModel()
        {
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            AcceptCommand = new RelayCommand(ExecuteAcceptAndClose);
        }

        private async void ExecuteAcceptAndClose()
        {
            if (_userDefinedKey.Equals(string.Empty)) 
                RequestClose?.Invoke();
            else
            {
                // Validate the key
                if (await ValidateUserDefinedKey(_userDefinedKey))
                    RequestAcceptAndClose?.Invoke();
                else
                    MessageBox.Show("Invalid key. Please enter a valid key.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static async Task<bool> ValidateUserDefinedKey(string key)
        {
            // Implement your validation logic here
            // For example, check if the key meets certain criteria
            // Return true if valid, false otherwise
            await Task.Delay(100); // Simulate async work
            return !string.IsNullOrWhiteSpace(key); // Example validation
        }
    }
}
