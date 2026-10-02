using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Xunit;

namespace Tjidde.Logging.Tests;

// The stores are isolated, but the end-to-end tests capture Console output, so they share the sequential collection.
[Collection("ConsoleOutput")]
public sealed class MaskedKeysStoreTests
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

    private static ServiceProvider BuildIsolatedHost()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddTjiddeLogger().UseIsolatedMaskedKeys());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void GetKeys_ReturnsTheSameImmutableSnapshotUntilTheKeysChange()
    {
        var store = new MaskedKeysStore();
        var empty = store.GetKeys();
        store.GetKeys().Should().BeSameAs(empty);

        store.Add("customerCode", "  ", null!);
        var afterAdd = store.GetKeys();
        afterAdd.Should().NotBeSameAs(empty).And.BeEquivalentTo("customerCode");
        empty.Should().BeEmpty();

        // No change, no new snapshot
        store.Add("CUSTOMERCODE");
        store.Remove("unknown");
        store.GetKeys().Should().BeSameAs(afterAdd);

        store.Remove("customercode");
        store.GetKeys().Should().BeEmpty();
        afterAdd.Should().BeEquivalentTo("customerCode");

        store.Add("a", "b");
        store.Clear();
        store.GetKeys().Should().BeEmpty();
    }

    [Fact]
    public void UseIsolatedMaskedKeys_RegistersOneStoreAsTheAccessor_RegardlessOfOrder()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.UseIsolatedMaskedKeys().AddTjiddeLogger());
        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<MaskedKeysStore>();
        provider.GetRequiredService<IMaskedKeysAccessor>().Should().BeSameAs(store);

        using var defaultProvider = new ServiceCollection()
            .AddLogging(builder => builder.AddTjiddeLogger())
            .BuildServiceProvider();
        defaultProvider.GetRequiredService<IMaskedKeysAccessor>().Should().BeOfType<GlobalMaskedKeysAccessor>();
    }

    [Fact]
    public void TwoHosts_WithTheirOwnStore_MaskIndependently()
    {
        var codeA = $"code-a-{Guid.NewGuid():N}";
        var codeB = $"code-b-{Guid.NewGuid():N}";

        using var hostA = BuildIsolatedHost();
        using var hostB = BuildIsolatedHost();
        var storeA = hostA.GetRequiredService<MaskedKeysStore>();
        var storeB = hostB.GetRequiredService<MaskedKeysStore>();
        storeA.Should().NotBeSameAs(storeB);

        storeA.Add(codeA);
        storeB.Add(codeB);

        var maskerA = MaskerOf(hostA);
        var maskerB = MaskerOf(hostB);

        // " | " keeps the "key value" pattern of one code from masking the other
        maskerA.MaskMessage($"{codeA} | {codeB}").Should().Be($"[REDACTED] | {codeB}");
        maskerB.MaskMessage($"{codeA} | {codeB}").Should().Be($"{codeA} | [REDACTED]");
        maskerA.IsSensitiveKey(codeB).Should().BeFalse();
        maskerB.IsSensitiveKey(codeB).Should().BeTrue();

        // Nothing leaks into the process-wide context
        MaskedKeysContext.GetKeys().Should().NotContain(codeA).And.NotContain(codeB);
    }

    [Fact]
    public void StoreChanges_ApplyToLoggersThatAlreadyExist()
    {
        var code = $"code-{Guid.NewGuid():N}";
        var otherCode = $"other-{Guid.NewGuid():N}";

        var output = CaptureConsoleOutput(() =>
        {
            using var host = BuildIsolatedHost();
            using var otherHost = BuildIsolatedHost();
            var store = host.GetRequiredService<MaskedKeysStore>();
            var logger = host.GetRequiredService<ILogger<MaskedKeysStoreTests>>();
            var otherLogger = otherHost.GetRequiredService<ILogger<MaskedKeysStoreTests>>();
            otherHost.GetRequiredService<MaskedKeysStore>().Add(otherCode);

            logger.LogInformation("before add: " + code);
            store.Add(code);
            logger.LogInformation("after add: " + code + " | " + otherCode);
            otherLogger.LogInformation("other host: " + code + " | " + otherCode);
            store.Remove(code);
            logger.LogInformation("after remove: " + code);
        });

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4);
        lines.Should().Contain(line => line.EndsWith($"before add: {code}"));
        lines.Should().Contain(line => line.EndsWith($"after add: [REDACTED] | {otherCode}"));
        lines.Should().Contain(line => line.EndsWith($"other host: {code} | [REDACTED]"));
        lines.Should().Contain(line => line.EndsWith($"after remove: {code}"));
    }

    private static ISensitiveDataMasker MaskerOf(IServiceProvider services)
    {
        var provider = services.GetServices<ILoggerProvider>().OfType<TjiddeLoggerProvider>().Single();
        return ((TjiddeLogger)provider.CreateLogger("Isolation")).Configuration.Masker;
    }
}
