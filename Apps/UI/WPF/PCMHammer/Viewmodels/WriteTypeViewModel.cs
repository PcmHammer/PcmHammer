using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class WriteTypeViewModel : ViewModelBase
    {
        public ObservableCollection<WriteType> WriteTypes { get; }
        public ObservableCollection<PcmType> PCMTypes { get; }

        // Properties
        private WriteType _selectedWriteType;
        public WriteType SelectedWriteType
        {
            get => _selectedWriteType;
            set => SetProperty(ref _selectedWriteType, value);
        }
        private PcmType _selectedPcmType;
        public PcmType SelectedPCMType
        {
            get => _selectedPcmType;
            set => SetProperty(ref _selectedPcmType, value);
        }
        // TODO: This property will be used to silence the brick warning when using the "reset pin" to bypass the standard initialization sequence
        private bool _isRecoveryMode;
        public bool IsRecoveryMode
        {
            get => _isRecoveryMode;
            set => SetProperty(ref _isRecoveryMode, value);
        }

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        // Commands
        public ICommand CloseCommand { get; }
        public ICommand AcceptAndCloseCommand { get; }

        // Constructor
        public WriteTypeViewModel()
        {
            WriteTypes = [WriteType.Full, WriteType.OsPlusCalibrationPlusBoot, WriteType.Parameters];

            PCMTypes = [.. Enum.GetValues<PcmType>()];

            CloseCommand = new RelayCommand(() => { RequestClose?.Invoke(); });
            AcceptAndCloseCommand = new RelayCommand(() => { RequestAcceptandClose?.Invoke(); });
        }
    }
}
