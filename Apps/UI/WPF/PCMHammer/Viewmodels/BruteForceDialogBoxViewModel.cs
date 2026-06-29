using System.ComponentModel;

namespace PCMHammer.Viewmodels
{
    public partial class BruteForceDialogBoxViewModel : INotifyPropertyChanged
    {


        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string propertyName) => 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
