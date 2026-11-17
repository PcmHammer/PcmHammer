using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCMHammerAvalonia.ViewModels;

public partial class UserDefinedKeyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userDefinedKey = "0000";

    public event Action<bool>? RequestClose;

    public UserDefinedKeyViewModel() { }

    public UserDefinedKeyViewModel(string initialKey)
    {
        UserDefinedKey = initialKey;
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private async Task AcceptAsync()
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

    public static bool ValidateUserDefinedKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        return int.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int val)
               && val is >= 0 and <= 0xFFFF;
    }
}