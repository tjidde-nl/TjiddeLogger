using Tjidde.Logging.Extensions;
using Tjidde.Logging.TuiApp.Services;
using Tjidde.Logging.TuiApp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Tjidde.Logging.TuiApp;

class Program
{
    static void Main(string[] args)
    {
        using var app = Application.Create();
        app.Init();

        var services = ConfigureServices(app);

        var mainView = services.GetRequiredService<MainView>();
        var root = new Window { Title = "Tjidde Logger TUI" };
        mainView.Build(root);

        app.Run(root);
    }

    private static ServiceProvider ConfigureServices(IApplication app)
    {
        var services = new ServiceCollection();

        services.AddSingleton(app);
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<MainView>();

        // Entries go to the built-in in-memory sink, which the log pane reads. The console sink is off:
        // console output would draw over the terminal UI.
        services.AddLogging(builder =>
        {
            builder
                .AddTjiddeLogger(options => options.WriteToConsole = false)
                .AddTjiddeInMemorySink();
        });

        return services.BuildServiceProvider();
    }
}
