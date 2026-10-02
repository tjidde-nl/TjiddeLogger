using FluentAssertions;
using Tjidde.Logging.Context;
using Xunit;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tjidde.Logging.Tests;

public sealed class TjiddeLoggingRegistrationTests
{
    [Fact]
    public void AddTjiddeLogger_RegistersProviderInDI()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger());

        using var provider = services.BuildServiceProvider();
        var loggerProviders = provider.GetServices<ILoggerProvider>();

        loggerProviders.Should().ContainSingle(p => p is TjiddeLoggerProvider);
    }

    [Fact]
    public void AddTjiddeLogger_RegistersDefaultCustomerContextAccessor()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger());

        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetService<ICustomerContextAccessor>();

        accessor.Should().NotBeNull();
        accessor.Should().BeOfType<AsyncLocalCustomerContextAccessor>();
    }

    [Fact]
    public void AddTjiddeLogger_WithOptions_AppliesConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger(options =>
        {
            options.IncludeScopes = false;
            options.MaskPlaceholder = "***";
            options.EnableSensitiveDataMasking = true;
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TjiddeLoggerOptions>>().Value;

        options.IncludeScopes.Should().BeFalse();
        options.MaskPlaceholder.Should().Be("***");
        options.EnableSensitiveDataMasking.Should().BeTrue();
    }

    [Fact]
    public void AddTjiddeLogger_CalledTwice_DoesNotDuplicateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddTjiddeLogger();
            builder.AddTjiddeLogger();
        });

        using var provider = services.BuildServiceProvider();
        var loggerProviders = provider.GetServices<ILoggerProvider>()
            .Where(p => p is TjiddeLoggerProvider)
            .ToList();

        loggerProviders.Should().HaveCount(1);
    }

    [Fact]
    public void AddTjiddeLogger_CreatesLoggerViaFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger());

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger<TjiddeLoggingRegistrationTests>();

        logger.Should().NotBeNull();
    }

    [Fact]
    public void AddTjiddeLogger_CustomAccessor_IsNotOverridden()
    {
        var services = new ServiceCollection();
        // Register a custom accessor before AddTjiddeLogger
        services.AddSingleton<ICustomerContextAccessor, CustomTestAccessor>();
        services.AddLogging(builder => builder.AddTjiddeLogger());

        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ICustomerContextAccessor>();

        accessor.Should().BeOfType<CustomTestAccessor>();
    }

#pragma warning disable CS0618 // Obsolete MinimumLevel/CategoryMinimumLevels: verifies the legacy filter still works until 2.0
    [Fact]
    public void AddTjiddeLogger_WithConfiguration_BindsOptionsFromSection()
    {
        var settings = new Dictionary<string, string?>
        {
            ["TjiddeLogger:OutputFormat"] = "Json",
            ["TjiddeLogger:MinimumLevel"] = "Warning",
            ["TjiddeLogger:CategoryMinimumLevels:Default"] = "Error",
            ["TjiddeLogger:CategoryMinimumLevels:My.Namespace"] = "Information",
            ["TjiddeLogger:MaskPlaceholder"] = "***",
            ["TjiddeLogger:EnableOpenTelemetryExport"] = "true",
            ["TjiddeLogger:OpenTelemetryActivitySourceName"] = "Tjidde.Logging.Tests",
            ["TjiddeLogger:OpenTelemetryCreateFallbackActivity"] = "true"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger(configuration));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TjiddeLoggerOptions>>().Value;

        options.OutputFormat.Should().Be(TjiddeLogOutputFormat.Json);
        options.MinimumLevel.Should().Be(LogLevel.Warning);
        options.MaskPlaceholder.Should().Be("***");
        options.EnableOpenTelemetryExport.Should().BeTrue();
        options.OpenTelemetryActivitySourceName.Should().Be("Tjidde.Logging.Tests");
        options.OpenTelemetryCreateFallbackActivity.Should().BeTrue();
        options.CategoryMinimumLevels["Default"].Should().Be(LogLevel.Error);
        options.CategoryMinimumLevels["My.Namespace"].Should().Be(LogLevel.Information);
    }
#pragma warning restore CS0618

    private sealed class CustomTestAccessor : ICustomerContextAccessor
    {
        public string? GetCustomerContext() => "TestCustomer";
    }
}
