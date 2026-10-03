using Avalonia;
using Tjidde.Logging.DesktopApp.ViewModels;
using Tjidde.Logging.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.DesktopApp;

internal static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();

        // Logging: Tjidde logger (console) + the built-in in-memory sink, which the UI reads
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder
                .AddTjiddeLogger(options =>
                {
                    options.IncludeScopes = true;
                })
                .AddTjiddeInMemorySink();
        });

        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
