using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class ChangeVinViewModel : ObservableObject
    {
        #region Actions
        public event Action? RequestCloseOk;
        public event Action? RequestCloseCancel;
        #endregion

        #region Properties
        [ObservableProperty]
        public partial string Vin { get; set; } = string.Empty;

        partial void OnVinChanging(string value)
        {
            // Sanitize input before the property updates
            string upperValue = value?.ToUpper() ?? string.Empty;

            if (ValidateVin(upperValue))
            {
                // Update backing field directly when valid
                Vin = upperValue;
            }
        }

        [ObservableProperty]
        private partial string ValidationPrompt { get; set; } = "Enter a 17-character VIN.";

        [ObservableProperty]
        private partial bool IsValid { get; set; }
        #endregion

        #region Commands
        // Do NOT name these methods "...Command": the generator appends "Command" to the method
        // name, so OkCommand() produced OkCommandCommand and the XAML binding to OkCommand
        // silently resolved to nothing - OK did nothing at all, and Cancel only appeared to work
        // because IsCancel="True" closes the dialog by itself. Same trap as WriteTypeViewModel.
        [RelayCommand(CanExecute = nameof(IsValid))]
        public void Ok() => RequestCloseOk?.Invoke();
        [RelayCommand]
        public void Cancel() => RequestCloseCancel?.Invoke();
        #endregion

        public ChangeVinViewModel(string initialVin)
        {
            Vin = initialVin;
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