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

        partial void OnVinChanged(string value)
        {
            // Assess whatever the user typed; never reject the edit. The old version only assigned the
            // backing field when the VIN was valid, which made a non-standard VIN impossible to type.
            Assess(value);
        }

        [ObservableProperty]
        private partial string ValidationPrompt { get; set; } = "Enter a 17-character VIN.";

        /// <summary>
        /// Whether the VIN can be written: 17 characters. A VIN that fails the standard's check digit
        /// is still writable, because CAN PCMs are routinely found with one (see VinAssessment).
        /// </summary>
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
            Assess(Vin);
        }

        /// <summary>
        /// Update the prompt and the OK gate from the shared assessment.
        /// </summary>
        private void Assess(string vin)
        {
            VinAssessment assessment = VinAssessment.Of(vin);
            ValidationPrompt = assessment.Message;
            IsValid = assessment.CanWrite;
            OkCommand.NotifyCanExecuteChanged();
        }
    }
}