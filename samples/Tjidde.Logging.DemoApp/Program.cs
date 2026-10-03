using Tjidde.Logging.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Tjidde logger: writes formatted output to the console, and to the built-in in-memory sink
// (the last 200 entries) so the Blazor UI can display them. Inject InMemoryLogSink to read them.
builder.Logging.ClearProviders();
builder.Logging
    .AddTjiddeLogger(options =>
    {
        options.IncludeScopes = true;
    })
    .AddTjiddeInMemorySink(capacity: 200);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<Tjidde.Logging.DemoApp.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
