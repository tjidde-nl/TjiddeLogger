using Tjidde.Logging.DemoApp.Logging;
using Tjidde.Logging.Extensions;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register the in-memory sink as a singleton so the UI can read from it
builder.Services.AddSingleton<InMemoryLogSink>();

// Add Tjidde logger (writes formatted output to the console)
// Add our in-memory provider so the Blazor UI can also display entries
builder.Logging.ClearProviders();
builder.Logging.AddTjiddeLogger(options =>
{
    options.IncludeScopes = true;
});
builder.Services.AddSingleton<ILoggerProvider>(sp =>
    new InMemoryLoggerProvider(sp.GetRequiredService<InMemoryLogSink>()));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<Tjidde.Logging.DemoApp.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
