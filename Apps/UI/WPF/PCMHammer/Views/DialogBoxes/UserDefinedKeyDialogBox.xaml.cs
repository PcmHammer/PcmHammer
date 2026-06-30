using PCMHammer.Viewmodels;
using System.Windows;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for UserDefinedKeyDialogBox.xaml
    /// </summary>
    public partial class UserDefinedKeyDialogBox : Window
    {
        public UserDefinedKeyDialogBox()
        {
            InitializeComponent();
            DataContext = new UserDefinedKeyDialogBoxViewModel();
        }
    }
}
