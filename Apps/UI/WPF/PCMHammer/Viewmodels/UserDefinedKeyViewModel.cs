using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMHammer.Helpers;
using System.Windows;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class UserDefinedKeyViewModel : ObservableObject
    {
        // Properties
        [ObservableProperty]
        public partial string UserDefinedKey {  get; set; } = string.Empty;

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptAndClose;

        // Commands
        [RelayCommand]
        public void Cancel() => RequestClose?.Invoke();
        public async Task Accept()
        {
            if (UserDefinedKey.Equals(string.Empty))
                RequestClose?.Invoke();
            else
            {
                // Validate the key
                if (await ValidateUserDefinedKey(UserDefinedKey))
                    RequestAcceptAndClose?.Invoke();
                else
                    MessageBox.Show("Invalid key. Please enter a valid key.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public UserDefinedKeyViewModel() { }

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
