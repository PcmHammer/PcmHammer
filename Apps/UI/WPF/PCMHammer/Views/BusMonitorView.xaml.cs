// SPDX-License-Identifier: GPL-3.0-only
using System.Windows.Controls;

namespace PCMHammer.Views
{
    /// <summary>
    /// Interaction logic for BusMonitorView.xaml. Auto-scroll lives here because it is a property of
    /// the TextBox, not the text, as with the Results and Debug logs in MainWindow.
    /// </summary>
    public partial class BusMonitorView : UserControl
    {
        public BusMonitorView()
        {
            InitializeComponent();
        }

        private void MonitorLogTextBox_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }

        private void MonitorLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
