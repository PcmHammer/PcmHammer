using Microsoft.UI.Xaml.Input;

namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingPage : Page
{
    public DataLoggingPage()
    {
        this.InitializeComponent();
    }

    private async void ListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        RecentFileListItem? recentFile = this.RecentFiles.SelectedItem as RecentFileListItem;
        if (recentFile == null)
        {
            return;
        }

        Task task = (this.DataContext as DataLoggingViewModel)?.Model?.RecentProfileClicked(recentFile) ?? Task.CompletedTask;
        await task;
    }

    /// <summary>
    /// Open a log profile when the user selects a profile and presses space or enter.
    /// </summary>
    /// <remarks>
    /// This requires the PreviewKeyDown event because the KeyDown event is not raised for the space or enter keys.
    /// </remarks>
    private async void ListView_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if ((eventArgs.Key != Windows.System.VirtualKey.Enter) && (eventArgs.Key != Windows.System.VirtualKey.Space))
        {
            return;
        }
            
        RecentFileListItem? recentFile = this.RecentFiles.SelectedItem as RecentFileListItem;
        if (recentFile == null)
        {
            return;
        }

        Task task = (this.DataContext as DataLoggingViewModel)?.Model?.RecentProfileClicked(recentFile) ?? Task.CompletedTask;
        await task;
    }
}

