using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for BruteForceDialogBox.xaml
    /// </summary>
    public partial class BruteForceDialogBox : Window
    {
        public BruteForceDialogBox()
        {
            InitializeComponent();
            DataContext = new BruteForceDialogBoxViewModel();
        }
    }
}
