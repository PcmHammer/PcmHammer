// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class MenuPage : Page
{
    private MenuModel? model;
    public MenuPage()
    {
        this.InitializeComponent();

        // This would be the usual way to set the DataContext, but there's a race condition.
        // App.GetService depends on App.Host.
        // App.Host is not initalized until the app has booted, including display of the main page.
        // The Menu page is embedded in the main page.
        // So the service locator can't be used in this case.
        // To work around this, the code that would normally be in MenuModel has been moved to MainModel.
        //this.DataContext = App.GetService<MenuViewModel>();

        this.DataContextChanged += (sender, e) =>
        {
            this.model = (this.DataContext as MenuViewModel)?.Model as MenuModel ?? this.model;
        };
    }

    // There seems to be a bug with Uno where buttons seem reachable after a navigated page has covered them. I feel this is the most elegant solution, but it could be worth investigating further if this is a known issue or if there's a better workaround.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        model?.ButtonStateControl(true);
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        model?.ButtonStateControl(false);
    }
}

