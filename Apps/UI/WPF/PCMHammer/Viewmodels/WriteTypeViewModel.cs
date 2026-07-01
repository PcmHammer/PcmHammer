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

            PCMTypes = [];
            foreach (PcmType pcmType in Enum.GetValues<PcmType>())
                PCMTypes.Add(pcmType);

            CloseCommand = new RelayCommand(() => { RequestClose?.Invoke(); });
            AcceptAndCloseCommand = new RelayCommand(() => { RequestAcceptandClose?.Invoke(); });
        }
    }
}
