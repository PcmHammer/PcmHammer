using Uno.UI.Hosting;

namespace PcmHacking.UnoUI;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWindows()
            .Build()
            .Run();
    }
}
