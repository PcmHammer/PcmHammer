// SPDX-License-Identifier: GPL-3.0-only
using PcmHacking.UnoUI.Services;
using Windows.UI.Core;
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class MainPage : Page
{

    private Windows.System.Display.DisplayRequest _displayRequest = null!;

    public MainPage()
    {
        this.InitializeComponent();
        this.ContentFrame.Navigate(typeof(MenuPage));
#if ANDROID
        SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
#endif
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        XamlRootService.Initialize(this.XamlRoot!);
        _displayRequest = new Windows.System.Display.DisplayRequest();
#if ANDROID
        _displayRequest.RequestActive();
#endif
    }

    private void MainPage_BackRequested(object? sender, BackRequestedEventArgs e)
    {
        if (MainModel.CanGoBack)
        {
            e.Handled = true;
            this.ContentFrame.GoBack();
            return;
        }

        // Never leave while a read or write holds the connection, wherever we are in the app.
        if (MainModel.OperationInProgress)
        {
            e.Handled = true;
            return;
        }

        // At the main menu with nothing running: leave it unhandled so Android finishes the activity
        // and closes the app. This used to be handled unconditionally, which swallowed every back
        // press at the root - there was no way out of the app short of force-stopping it. Finishing
        // runs MainActivity.OnDestroy, which releases the connection.
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
#if ANDROID
            _displayRequest.RequestRelease();
            SystemNavigationManager.GetForCurrentView().BackRequested -= MainPage_BackRequested;
#endif
    }

    public void FrameNavigated(object sender, NavigationEventArgs e)
    {
        (this.DataContext as MainViewModel)?.FrameNavigated.Execute(null);
    }
}
