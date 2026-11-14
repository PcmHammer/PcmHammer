using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PCMLoggerAvalonia.ViewModels;
using PCMLoggerAvalonia.Views;

namespace PCMLoggerAvalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Desktop: host MainView in a Window. MainView inherits the window's DataContext.
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            // Android: a fresh MainView is built per activity (e.g. after a rotation re-create).
            activity.MainViewFactory = () => new MainView { DataContext = new MainWindowViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Other single-view platforms: MainView is the root control (no Window).
            singleView.MainView = new MainView { DataContext = new MainWindowViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
