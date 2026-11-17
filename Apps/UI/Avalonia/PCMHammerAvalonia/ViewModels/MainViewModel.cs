using CommunityToolkit.Mvvm.ComponentModel;
using PCMHammerAvalonia.ViewModels;

namespace PCMHammerAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{

    [ObservableProperty]
    public partial string ResultsLog { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string DebugLog { get; set; } = string.Empty;
}