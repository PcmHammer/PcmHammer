using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.ViewModels;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public class ChangeVinViewModel : ViewModelBase
    {
        public event Action? RequestCloseOk;
        public event Action? RequestCloseCancel;

        private string _vin = string.Empty;
        public string Vin
        {
            get => _vin;
            set
            {
                string upperValue = value?.ToUpper() ?? string.Empty;
                if (ValidateVin(upperValue))
                    SetProperty(ref _vin, upperValue);
            }
        }

        private string _validationPrompt = "Enter a 17-character VIN.";
        public string ValidationPrompt
        {
            get => _validationPrompt;
            private set => SetProperty(ref _validationPrompt, value);
        }

        private bool _isValid;
        public bool IsValid
        {
            get => _isValid;
            private set => SetProperty(ref _isValid, value);
        }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public ChangeVinViewModel(string initialVin)
        {
            Vin = initialVin;

            OkCommand = new RelayCommand(
                execute: () => RequestCloseOk?.Invoke(),
                canExecute: () => IsValid
            );

            CancelCommand = new RelayCommand(
                execute: () => RequestCloseCancel?.Invoke()
            );

            ValidateVin(Vin);
        }

        private bool ValidateVin(string vin)
        {
            if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17)
            {
                ValidationPrompt = $"The VIN must be 17 characters long.\nThis is {vin?.Length ?? 0} characters.";
                IsValid = false;
                return false;
            }

            // Call legacy PcmHacking.VinValidator utility
            if (VinValidator.IsValid(vin, out int invalidCharacterIndex, out char requiredCheckDigit))
            {
                ValidationPrompt = "The VIN is valid. Good!";
                IsValid = true;
            }
            else
            {
                IsValid = false;
                if (invalidCharacterIndex >= 0)
                {
                    char invalidCharacter = vin[invalidCharacterIndex];
                    ValidationPrompt = $"The \"{invalidCharacter}\" at position {invalidCharacterIndex + 1} is not a letter or number.";
                }
                else if (requiredCheckDigit != 'X')
                    ValidationPrompt = $"The VIN check digit on position 9 is incorrect.\nCorrect check digit is: {requiredCheckDigit}";
                else
                    ValidationPrompt = "The VIN is invalid.";
            }
            return IsValid;
        }
    }
}