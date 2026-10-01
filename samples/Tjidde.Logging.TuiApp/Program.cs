using Tjidde.Logging.Extensions;
using Tjidde.Logging.TuiApp.Logging;
using Tjidde.Logging.TuiApp.Services;
using Tjidde.Logging.TuiApp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Terminal.Gui;

namespace Tjidde.Logging.TuiApp;

class Program
{
    static void Main(string[] args)
    {
        InitializeTerminal();

        var services = ConfigureServices();

        var mainView = services.GetRequiredService<MainView>();
        mainView.Build(Application.Top);

        Application.Run();
        Application.Shutdown();
    }

    private static ServiceProvider ConfigureServices()
    {
        var colorScheme = CreateColorScheme();
        var sink = new InMemoryLogSink();

        var services = new ServiceCollection();

        services.AddSingleton(sink);
        services.AddSingleton(colorScheme);
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<MainView>();

        services.AddSingleton<ILoggerProvider>(new InMemoryLoggerProvider(sink));
        services.AddLogging(builder =>
        {
            builder.AddTjiddeLogger();
        });

        return services.BuildServiceProvider();
    }

    private static void InitializeTerminal()
    {
        Application.UseSystemConsole = true;
        Application.Init();

        Application.Top.WantMousePositionReports = false;
        Application.Top.WantContinuousButtonPressed = false;

        if (Application.Driver != null)
        {
            Console.Write("\x1b[?1000l");
            Console.Write("\x1b[?1001l");
            Console.Write("\x1b[?1002l");
            Console.Write("\x1b[?1003l");
        }

        Application.RootMouseEvent = _ => { };
    }

    private static ColorScheme CreateColorScheme() => new()
    {
        Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black),
        Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.White),
        HotNormal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
        HotFocus = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.White),
        Disabled = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black),
    };
}
