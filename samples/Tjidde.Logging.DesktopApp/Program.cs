using Avalonia;
using Tjidde.Logging.DesktopApp.Logging;
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

        // Shared in-memory sink
        var sink = new InMemoryLogSink();
        services.AddSingleton(sink);

        // Logging: Tjidde logger + in-memory capture
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddTjiddeLogger(options =>
            {
                options.IncludeScopes = true;
            });
            builder.Services.AddSingleton<ILoggerProvider>(sp =>
                new InMemoryLoggerProvider(sp.GetRequiredService<InMemoryLogSink>()));
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
