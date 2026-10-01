using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Masking;
using Xunit;

namespace Tjidde.Logging.Tests;

// MaskedKeysContext is global state, so these tests share the sequential console collection.
[Collection("ConsoleOutput")]
public sealed class MaskedKeysContextTests
{
    private static string CaptureConsoleOutput(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    [Fact]
    public void AddAndRemove_ApplyToLoggersThatAlreadyExist()
    {
        var code = $"code-{Guid.NewGuid():N}";
        try
        {
            var output = CaptureConsoleOutput(() =>
            {
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddTjiddeLogger());
                using var provider = services.BuildServiceProvider();
                var logger = provider.GetRequiredService<ILogger<MaskedKeysContextTests>>();

                logger.LogInformation("before add: " + code);
                MaskedKeysContext.Add(code);
                logger.LogInformation("after add: " + code);
                MaskedKeysContext.Remove(code);
                logger.LogInformation("after remove: " + code);
            });

            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCount(3);
            lines[0].Should().EndWith($"before add: {code}");
            lines[1].Should().EndWith("after add: [REDACTED]");
            lines[2].Should().EndWith($"after remove: {code}");
        }
        finally
        {
            MaskedKeysContext.Remove(code);
        }
    }

    [Fact]
    public void GetKeys_ReturnsTheSameSnapshotUntilTheKeysChange()
    {
        var code = $"code-{Guid.NewGuid():N}";
        try
        {
            var before = MaskedKeysContext.GetKeys();
            MaskedKeysContext.GetKeys().Should().BeSameAs(before);

            MaskedKeysContext.Add(code);
            var after = MaskedKeysContext.GetKeys();

            after.Should().NotBeSameAs(before);
            after.Should().Contain(code);
            before.Should().NotContain(code);
        }
        finally
        {
            MaskedKeysContext.Remove(code);
        }
    }
}
