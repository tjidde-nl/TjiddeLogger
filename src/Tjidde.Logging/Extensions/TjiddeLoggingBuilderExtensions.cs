using Tjidde.Logging.Context;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Tjidde.Logging.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tjidde.Logging.Extensions;

/// <summary>
/// Extension methods for registering the Tjidde logger with <see cref="ILoggingBuilder"/>.
/// </summary>
public static class TjiddeLoggingBuilderExtensions
{
    private const string DefaultConfigurationSectionPath = "TjiddeLogger";
    /// <summary>
    /// Adds the Tjidde logger to the logging pipeline using default options.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the logger to.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddTjiddeLogger(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddTjiddeLogger(_ => { });
    }

    /// <summary>
    /// Adds the Tjidde logger and binds <see cref="TjiddeLoggerOptions"/> from the provided configuration section.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the logger to.</param>
    /// <param name="configurationSection">Configuration section containing <see cref="TjiddeLoggerOptions"/> values.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddTjiddeLogger(this ILoggingBuilder builder, IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        // Rebind the options when the configuration reloads (for example appsettings.json with reloadOnChange),
        // so the provider applies the new values to existing loggers.
        builder.Services.AddSingleton<IOptionsChangeTokenSource<TjiddeLoggerOptions>>(
            new ConfigurationSectionChangeTokenSource(configurationSection));

        return builder.AddTjiddeLogger(options => configurationSection.Bind(options));
    }

    /// <summary>
    /// Adds the Tjidde logger and binds options from a named configuration section.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the logger to.</param>
    /// <param name="configuration">Root configuration.</param>
    /// <param name="sectionPath">Section path with Tjidde logger options. Default: TjiddeLogger.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddTjiddeLogger(this ILoggingBuilder builder, IConfiguration configuration, string sectionPath = DefaultConfigurationSectionPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return builder.AddTjiddeLogger(configuration.GetSection(sectionPath));
    }

    /// <summary>
    /// Adds the Tjidde logger to the logging pipeline with custom options.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/> to add the logger to.</param>
    /// <param name="configure">A delegate to configure <see cref="TjiddeLoggerOptions"/>.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddTjiddeLogger(this ILoggingBuilder builder, Action<TjiddeLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);

        // Register the default customer context accessor if none has been registered by the application
        builder.Services.TryAddSingleton<ICustomerContextAccessor, AsyncLocalCustomerContextAccessor>();

        // Register the default masked keys accessor
        builder.Services.TryAddSingleton<IMaskedKeysAccessor, GlobalMaskedKeysAccessor>();

        // Register the provider through a factory, so the constructor choice never depends on the container:
        // a TimeProvider registered in DI is used for timestamps, otherwise TimeProvider.System.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, TjiddeLoggerProvider>(CreateProvider));

        return builder;
    }

    /// <summary>
    /// Configures the Tjidde logger to use the structured JSON format.
    /// This is a convenience method that sets <see cref="TjiddeLoggerOptions.OutputFormat"/> to <see cref="TjiddeLogOutputFormat.Json"/>.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/>.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder UseTjiddeJsonFormat(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<TjiddeLoggerOptions>(options => options.OutputFormat = TjiddeLogOutputFormat.Json);
        return builder;
    }

    /// <summary>
    /// Configures the Tjidde logger to use the human-readable text format.
    /// This is a convenience method that sets <see cref="TjiddeLoggerOptions.OutputFormat"/> to <see cref="TjiddeLogOutputFormat.Text"/>.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/>.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder UseTjiddeTextFormat(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<TjiddeLoggerOptions>(options => options.OutputFormat = TjiddeLogOutputFormat.Text);
        return builder;
    }

    /// <summary>
    /// Gives this host its own <see cref="MaskedKeysStore"/> instead of the process-wide <see cref="MaskedKeysContext"/>.
    /// The store is registered as a singleton; inject <see cref="MaskedKeysStore"/> to add or remove keys. Changes apply
    /// immediately to the loggers of this host only, so hosts and tests in the same process do not share keys.
    /// Replaces any <see cref="IMaskedKeysAccessor"/> registered earlier; can be called before or after <c>AddTjiddeLogger</c>.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/>.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder UseIsolatedMaskedKeys(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<MaskedKeysStore>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IMaskedKeysAccessor>(
            services => services.GetRequiredService<MaskedKeysStore>()));
        return builder;
    }

    /// <summary>
    /// Adds <typeparamref name="TSink"/> as an extra destination for Tjidde log entries. The sink is registered as a
    /// singleton under its own type (so it can be injected) and as <see cref="ILogSink"/>; registering the same type
    /// twice has no effect. Sinks are called synchronously on the logging thread and must be fast and thread-safe.
    /// Sinks registered directly with <c>services.AddSingleton&lt;ILogSink, ...&gt;()</c> are used as well.
    /// </summary>
    /// <typeparam name="TSink">The sink type, created by the container.</typeparam>
    /// <param name="builder">The <see cref="ILoggingBuilder"/>.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddTjiddeSink<TSink>(this ILoggingBuilder builder)
        where TSink : class, ILogSink
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<TSink>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILogSink, TSink>(services => services.GetRequiredService<TSink>()));
        return builder;
    }

    /// <summary>
    /// Adds <paramref name="sink"/> as an extra destination for Tjidde log entries. The instance is registered as
    /// <see cref="ILogSink"/> and, unless that type is already registered, under <typeparamref name="TSink"/>, so it
    /// can be injected. Sinks are called synchronously on the logging thread and must be fast and thread-safe.
    /// The container does not dispose an instance passed here.
    /// </summary>
    /// <typeparam name="TSink">The sink type.</typeparam>
    /// <param name="builder">The <see cref="ILoggingBuilder"/>.</param>
    /// <param name="sink">The sink instance.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    public static ILoggingBuilder AddTjiddeSink<TSink>(this ILoggingBuilder builder, TSink sink)
        where TSink : class, ILogSink
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sink);
        builder.Services.TryAddSingleton(sink);
        builder.Services.AddSingleton<ILogSink>(sink);
        return builder;
    }

    /// <summary>
    /// Adds an <see cref="InMemoryLogSink"/> that keeps the last <paramref name="capacity"/> entries, for example to
    /// show them in a UI. Inject <see cref="InMemoryLogSink"/> to read them. Calling it again has no effect.
    /// </summary>
    /// <param name="builder">The <see cref="ILoggingBuilder"/>.</param>
    /// <param name="capacity">The maximum number of entries kept. Default: <see cref="InMemoryLogSink.DefaultCapacity"/>.</param>
    /// <returns>The <see cref="ILoggingBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public static ILoggingBuilder AddTjiddeInMemorySink(this ILoggingBuilder builder, int capacity = InMemoryLogSink.DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be at least 1.");

        builder.Services.TryAddSingleton(_ => new InMemoryLogSink(capacity));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILogSink, InMemoryLogSink>(services => services.GetRequiredService<InMemoryLogSink>()));
        return builder;
    }

    private static TjiddeLoggerProvider CreateProvider(IServiceProvider services)
        => new(
            services.GetRequiredService<IOptionsMonitor<TjiddeLoggerOptions>>(),
            services.GetRequiredService<ICustomerContextAccessor>(),
            services.GetRequiredService<IMaskedKeysAccessor>(),
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetServices<ILogSink>());

    /// <summary>Signals an options change when the bound configuration section reloads.</summary>
    private sealed class ConfigurationSectionChangeTokenSource : IOptionsChangeTokenSource<TjiddeLoggerOptions>
    {
        private readonly IConfiguration _configuration;

        public ConfigurationSectionChangeTokenSource(IConfiguration configuration) => _configuration = configuration;

        public string Name => Microsoft.Extensions.Options.Options.DefaultName;

        public IChangeToken GetChangeToken() => _configuration.GetReloadToken();
    }
}
