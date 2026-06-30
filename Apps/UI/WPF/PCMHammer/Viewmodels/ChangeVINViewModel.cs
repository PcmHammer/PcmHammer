using PcmHacking;
using PCMHammer.Helpers;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public class ChangeVinViewModel : INotifyPropertyChanged
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
                if (_vin != upperValue)
                {
                    _vin = upperValue;
                    OnPropertyChanged();
                    ValidateVin();
                }
            }
        }

        private string _validationPrompt = "Enter a 17-character VIN.";
        public string ValidationPrompt
        {
            get => _validationPrompt;
            private set { _validationPrompt = value; OnPropertyChanged(); }
        }

        private bool _isValid;
        public bool IsValid
        {
            get => _isValid;
            private set
            {
                _isValid = value;
                OnPropertyChanged();
                RelayCommand.RaiseCanExecuteChanged();
            }
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

            ValidateVin();
        }

        private void ValidateVin()
        {
            if (string.IsNullOrWhiteSpace(_vin) || _vin.Length != 17)
            {
                ValidationPrompt = $"The VIN must be 17 characters long.\nThis is {_vin?.Length ?? 0} characters.";
                IsValid = false;
                return;
            }

            // Call legacy PcmHacking.VinValidator utility
            if (VinValidator.IsValid(_vin, out int invalidCharacterIndex, out char requiredCheckDigit))
            {
                ValidationPrompt = "The VIN is valid. Good!";
                IsValid = true;
            }
            else
            {
                IsValid = false;
                if (invalidCharacterIndex >= 0)
                {
                    char invalidCharacter = _vin[invalidCharacterIndex];
                    ValidationPrompt = $"The \"{invalidCharacter}\" at position {invalidCharacterIndex + 1} is not a letter or number.";
                }
                else if (requiredCheckDigit != 'X')
                    ValidationPrompt = $"The VIN check digit on position 9 is incorrect.\nCorrect check digit is: {requiredCheckDigit}";
                else
                    ValidationPrompt = "The VIN is invalid.";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}