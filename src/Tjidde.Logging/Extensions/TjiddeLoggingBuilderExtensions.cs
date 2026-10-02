using Tjidde.Logging.Context;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
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

        // Register the provider
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, TjiddeLoggerProvider>());

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

    /// <summary>Signals an options change when the bound configuration section reloads.</summary>
    private sealed class ConfigurationSectionChangeTokenSource : IOptionsChangeTokenSource<TjiddeLoggerOptions>
    {
        private readonly IConfiguration _configuration;

        public ConfigurationSectionChangeTokenSource(IConfiguration configuration) => _configuration = configuration;

        public string Name => Microsoft.Extensions.Options.Options.DefaultName;

        public IChangeToken GetChangeToken() => _configuration.GetReloadToken();
    }
}
