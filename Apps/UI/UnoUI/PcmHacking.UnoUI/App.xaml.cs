using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Services;
using Uno.Resizetizer;
// using Windows.System;

namespace PcmHacking.UnoUI;
public partial class App : Application
{
    /// <summary>
    /// This is required for some features in native-Windows builds.
    /// </summary>
    /// <remarks>
    /// The 'references' count below might become inaccurate, due to conditional compilation.
    /// See SettingsModel.OpenLogFolderPicker for example.
    /// </remarks>
    public static Window? StaticMainWindow { get; private set; }
        
    private DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        App.InitializeLogger();
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    public static T GetService<T>() where T : class
    {
        App current = (App)Application.Current;

        T? result = current.Host?.Services.GetService(typeof(T)) as T;
        if (result == null)
        {
            throw new Exception($"Service {typeof(T).Name} not found.");
        }
        return result;
    }

    private static void InitializeLogger()
    {
#if DEBUG
        // Logging is disabled by default for release builds, as it incurs a significant
        // initialization cost from Microsoft.Extensions.Logging setup. If startup performance
        // is a concern for your application, keep this disabled. If you're running on web or
        // desktop targets, you can use url or command line parameters to enable it yourself.
        //
        // For more performance documentation: https://platform.uno/docs/articles/Uno-UI-Performance.html

        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
        builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
        builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());
#elif NETFX_CORE
        builder.AddDebug();
#else
            builder.AddConsole();
#endif
            // Exclude logs below this level
            builder.SetMinimumLevel(LogLevel.Information);

            // Default filters for Uno Platform namespaces
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);

            // Generic Xaml events
            // builder.AddFilter("Windows.UI.Xaml", LogLevel.Debug );
            // builder.AddFilter("Windows.UI.Xaml.VisualStateGroup", LogLevel.Debug );
            // builder.AddFilter("Windows.UI.Xaml.StateTriggerBase", LogLevel.Debug );
            // builder.AddFilter("Windows.UI.Xaml.UIElement", LogLevel.Debug );
            // builder.AddFilter("Windows.UI.Xaml.FrameworkElement", LogLevel.Trace );

            // Layouter specific messages
            // builder.AddFilter("Windows.UI.Xaml.Controls", LogLevel.Debug );
            // builder.AddFilter("Windows.UI.Xaml.Controls.Layouter", LogLevel.Debug );
            // builder.AddFilter("Windows.UI.Xaml.Controls.Panel", LogLevel.Debug );

            // builder.AddFilter("Windows.Storage", LogLevel.Debug );

            // Binding related messages
            builder.AddFilter("Windows.UI.Xaml.Data", LogLevel.Debug );

            // Binder memory references tracking
            // builder.AddFilter("Uno.UI.DataBinding.BinderReferenceHolder", LogLevel.Debug );

            // RemoteControl and HotReload related
            // builder.AddFilter("Uno.UI.RemoteControl", LogLevel.Information);

            // Debug JS interop
            // builder.AddFilter("Uno.Foundation.WebAssemblyRuntime", LogLevel.Debug );
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif

#endif // DEBUG
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .ConfigureServices(services =>
                {
                    services.AddSingleton<PcmHacking.ILogger, UnoUI.Services.ProgressLogger>();
                    services.AddSingleton<Services.IVehicleService, Services.VehicleService>();
                    services.AddSingleton<IMessenger, WeakReferenceMessenger>();
                    services.AddSingleton<ISettingsService, Services.SettingsService>();
                    services.AddSingleton<DispatcherQueue>(dispatcherQueue);
                    services.AddSingleton<MenuViewModel>();
                    services.AddSingleton<Services.VehicleService>();
                    
                })

                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);

                }, enableUnoLogging: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .ConfigureServices((context, services) =>
                {
                    // TODO: Register your services
                    //services.AddSingleton<IMyService, MyService>();
                })
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)
                .UseStorage()

            );
        MainWindow = builder.Window;
        StaticMainWindow = builder.Window;

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        Host = await builder.NavigateAsync<Shell>();
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellModel)),
            new ViewMap<MainPage, MainModel>(),
            new ViewMap<MenuPage, MenuModel>(),
            new DataViewMap<SecondPage, SecondModel, Entity>(),
            new ViewMap<SettingsPage, SettingsModel>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellModel>(),
                Nested:
                [
                    new ("Main", View: views.FindByViewModel<MainModel>(), IsDefault:true),
                    new ("Second", View: views.FindByViewModel<SecondModel>()),
                    new ("Settings", View: views.FindByViewModel<SettingsModel>())
                ]
            )
        );
    }
}
