namespace PcmHacking.UnoUI.Presentation;

public sealed partial class MenuPage : Page
{
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
    }
}

