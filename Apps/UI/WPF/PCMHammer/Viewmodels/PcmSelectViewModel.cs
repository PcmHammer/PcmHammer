using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace PCMHammer.Viewmodels
{
    public partial class PcmSelectViewModel : ObservableObject
    {
        public ObservableCollection<PcmType> PCMTypes { get; }

        // Properties
        [ObservableProperty]
        public partial PcmType SelectedPCMType { get; set; }

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        // Commands
        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
        [RelayCommand]
        public void AcceptAndClose() => RequestAcceptandClose?.Invoke();

        // Constructor
        public PcmSelectViewModel() => PCMTypes = [.. Enum.GetValues<PcmType>()];
    }
}
