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
}

