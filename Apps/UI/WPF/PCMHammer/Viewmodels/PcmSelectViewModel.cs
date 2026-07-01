using PcmHacking;
using PCMHammer.Helpers;
using PCMHammer.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class PcmSelectViewModel : ViewModelBase
    {
        public ObservableCollection<PcmType> PCMTypes { get; }

        // Properties
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
        public PcmSelectViewModel()
        {
            PCMTypes = [.. Enum.GetValues<PcmType>()];

            CloseCommand = new RelayCommand(() => { RequestClose?.Invoke(); });
            AcceptAndCloseCommand = new RelayCommand(() => { RequestAcceptandClose?.Invoke(); });
        }
    }
}
