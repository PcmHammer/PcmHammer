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

        #endregion

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        // Commands. Do NOT name these methods "...Command": the generator appends "Command" to the
        // method name, so CloseCommand() would produce CloseCommandCommand and the XAML binding to
        // CloseCommand would silently resolve to nothing (which is why OK did nothing - Cancel only
        // appeared to work because IsCancel="True" closes the dialog by itself).
        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
        [RelayCommand]
        public void AcceptAndClose() => RequestAcceptandClose?.Invoke();

        // Constructor
        public WriteTypeViewModel()
        {
            WriteTypes = [WriteType.Full, WriteType.OsPlusCalibrationPlusBoot, WriteType.Parameters];

            PCMTypes = [.. Enum.GetValues<PcmType>()];
        }
    }
}
