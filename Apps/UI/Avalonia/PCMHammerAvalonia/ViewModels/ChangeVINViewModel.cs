using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;

namespace PCMHammerAvalonia.ViewModels;

public partial class ChangeVinViewModel : ObservableObject
{
    public event Action<bool>? RequestClose;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private string _vin = string.Empty;

    [ObservableProperty]
    private string _validationPrompt = "Enter a 17-character VIN.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private bool _isValid;

    public ChangeVinViewModel(string initialVin)
    {
        Vin = initialVin?.ToUpperInvariant().Trim() ?? string.Empty;
        ValidateVin(Vin);
    }

    partial void OnVinChanged(string value)
    {
        // Sanitize to uppercase without triggering re-entrant setter loops
        string upperValue = value?.ToUpperInvariant() ?? string.Empty;
        if (Vin != upperValue)
        {
            Vin = upperValue;
            return;
        }

        ValidateVin(Vin);
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Ok()
    {
        if (IsValid)
        {
            RequestClose?.Invoke(true);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private bool ValidateVin(string vin)
    {
        if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17)
        {
            ValidationPrompt = $"The VIN must be 17 characters long.\nThis is {vin?.Length ?? 0} characters.";
            IsValid = false;
            return false;
        }

        if (VinValidator.IsValid(vin, out int invalidCharacterIndex, out char requiredCheckDigit))
        {
            ValidationPrompt = "The VIN is valid.";
            IsValid = true;
        }
        else
        {
            IsValid = false;
            if (invalidCharacterIndex >= 0)
            {
                char invalidChar = vin[invalidCharacterIndex];
                ValidationPrompt = $"The '{invalidChar}' at position {invalidCharacterIndex + 1} is invalid.";
            }
            else if (requiredCheckDigit != 'X')
            {
                ValidationPrompt = $"The VIN check digit at position 9 is incorrect.\nExpected check digit: {requiredCheckDigit}";
            }
            else
            {
                ValidationPrompt = "The VIN is invalid.";
            }
        }

        return IsValid;
    }
}