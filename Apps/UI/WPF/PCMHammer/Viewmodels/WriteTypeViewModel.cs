using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Collections.ObjectModel;

namespace PCMHammer.Viewmodels
{
    public partial class WriteTypeViewModel : ObservableObject
    {
        public ObservableCollection<WriteType> WriteTypes { get; }
        public ObservableCollection<PcmType> PCMTypes { get; }

        #region Properties
        [ObservableProperty]
        public partial WriteType SelectedWriteType { get; set; }

        [ObservableProperty]
        public partial PcmType SelectedPCMType { get; set; }

        // TODO: This property will be used to silence the brick warning when using the "reset pin" to bypass the standard initialization sequence
        [ObservableProperty]
        public partial bool SuppressOSIDWarning { get; set; }
        #endregion

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        // Commands
        [RelayCommand]
        public void CloseCommand() => RequestClose?.Invoke();
        [RelayCommand]
        public void AcceptAndCloseCommand() => RequestAcceptandClose?.Invoke();

        // Constructor
        public WriteTypeViewModel()
        {
            WriteTypes = [WriteType.Full, WriteType.OsPlusCalibrationPlusBoot, WriteType.Parameters];

            PCMTypes = [.. Enum.GetValues<PcmType>()];
        }
    }
}
