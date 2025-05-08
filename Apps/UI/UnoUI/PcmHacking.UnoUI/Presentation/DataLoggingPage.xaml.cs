using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace PcmHacking.UnoUI.Presentation;

public sealed partial class DataLoggingPage : Page
{
    private int lastTapTime = 0;
    private Point lastTapPoint = new Point(0, 0);

    public DataLoggingPage()
    {
        this.InitializeComponent();
    }

    private async void ListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs? e)
    {
        RecentFileListItem? recentFile = this.RecentFiles.SelectedItem as RecentFileListItem;
        if (recentFile == null)
        {
            return;
        }

        Task task = (this.DataContext as DataLoggingViewModel)?.Model?.OpenRecentLogProfile(recentFile) ?? Task.CompletedTask;
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

        Task task = (this.DataContext as DataLoggingViewModel)?.Model?.OpenRecentLogProfile(recentFile) ?? Task.CompletedTask;
        await task;
    }

    private void RecentFiles_Tapped(object sender, TappedRoutedEventArgs e)
    {
        int currentTime = Environment.TickCount;
        Point currentPoint = e.GetPosition(this.RecentFiles);
        try
        {
            if (currentTime - lastTapTime > 250)
            {
                return;
            }

            const int tapRegionSize = 15;
            if (Math.Abs(currentPoint.X - lastTapPoint.X) > tapRegionSize || 
                Math.Abs(currentPoint.Y - lastTapPoint.Y) > tapRegionSize)
            {
                return;
            }

            this.ListView_DoubleTapped(sender, null);
        }
        finally
        {
            this.lastTapTime = currentTime;
            this.lastTapPoint = currentPoint;
        }
    }
}

