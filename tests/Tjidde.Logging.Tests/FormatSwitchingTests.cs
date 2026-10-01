using FluentAssertions;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tjidde.Logging.Tests;

public sealed class FormatSwitchingTests
{
    [Fact]
    public void UseTjiddeJsonFormat_SetsOutputFormatToJson()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddTjiddeLogger();
            builder.UseTjiddeJsonFormat();
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<TjiddeLoggerOptions>>().Value;

        // Assert
        options.OutputFormat.Should().Be(TjiddeLogOutputFormat.Json);
    }

    [Fact]
    public void UseTjiddeTextFormat_SetsOutputFormatToText()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            // Set to JSON first to ensure the switch works
            builder.AddTjiddeLogger(opt => opt.OutputFormat = TjiddeLogOutputFormat.Json);
            builder.UseTjiddeTextFormat();
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<TjiddeLoggerOptions>>().Value;

        // Assert
        options.OutputFormat.Should().Be(TjiddeLogOutputFormat.Text);
    }
}