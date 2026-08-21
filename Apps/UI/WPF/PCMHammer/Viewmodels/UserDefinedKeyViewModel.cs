using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCMHammer.Viewmodels;

public partial class UserDefinedKeyViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial string UserDefinedKey { get; set; } = "0000";

    public event Action<bool>? RequestClose;

    public UserDefinedKeyViewModel() { }

    public UserDefinedKeyViewModel(string initialKey) => UserDefinedKey = initialKey;

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);


    [RelayCommand(CanExecute = nameof(CanAccept))]
    private async Task Accept()
    {
        if (string.IsNullOrWhiteSpace(UserDefinedKey))
        {
            RequestClose?.Invoke(false);
            return;
        }

        if (ValidateUserDefinedKey(UserDefinedKey))
        {
            RequestClose?.Invoke(true);
        }
        else
        {
            // Clear or handle invalid state
            UserDefinedKey = "0000";
        }
    }
    private bool CanAccept() => ValidateUserDefinedKey(UserDefinedKey);

    public static bool ValidateUserDefinedKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (key.Length != 4) return false; // Ensure the key is exactly 4 characters

        return int.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int val)
               && val is >= 0 and <= 0xFFFF;
    }
}