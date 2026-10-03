using FluentAssertions;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Context;
using Tjidde.Logging.Logging;
using Tjidde.Logging.Masking;
using Tjidde.Logging.Options;
using Xunit;

namespace Tjidde.Logging.Tests;

/// <summary>
/// All loggers of a provider share one masker per configuration, and that masker stays correct when
/// MaskedKeysContext changes while other threads log.
/// </summary>
// MaskedKeysContext is global state and these tests capture Console output, so they share the sequential collection.
[Collection("ConsoleOutput")]
public sealed class SharedMaskerTests
{
    [Fact]
    public void Loggers_ShareOneMaskerPerConfiguration_AndSwitchTogetherOnOptionsChange()
    {
        var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions());
        using var provider = new TjiddeLoggerProvider(monitor, new AsyncLocalCustomerContextAccessor(), new GlobalMaskedKeysAccessor());
        var first = (TjiddeLogger)provider.CreateLogger("My.App.First");
        var second = (TjiddeLogger)provider.CreateLogger("My.App.Second");

        var before = first.Configuration;
        second.Configuration.Should().BeSameAs(before);

        monitor.Set(new TjiddeLoggerOptions { MaskPlaceholder = "***" });

        first.Configuration.Should().NotBeSameAs(before);
        first.Configuration.Masker.Should().NotBeSameAs(before.Masker);
        second.Configuration.Masker.Should().BeSameAs(first.Configuration.Masker);
        first.Configuration.Masker.MaskValue("password", "secret").Should().Be("***");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentLogging_WhileMaskedKeysChange_KeepsMaskingAndNeverThrows(bool useGlobalContext)
    {
        var id = Guid.NewGuid().ToString("N");
        var permanentKey = $"perm-{id}";
        var churnKeys = Enumerable.Range(0, 8).Select(i => $"churn-{id}-{i}").ToArray();
        const int threads = 8;
        const int entriesPerThread = 200;

        var original = Console.Out;
        using var writer = new StringWriter();
        var errors = new List<Exception>();
        // Both snapshot accessors: the process-wide MaskedKeysContext and an isolated MaskedKeysStore
        var store = new MaskedKeysStore();
        IMaskedKeysAccessor accessor = useGlobalContext ? new GlobalMaskedKeysAccessor() : store;
        Action<string[]> add = useGlobalContext ? MaskedKeysContext.Add : store.Add;
        Action<string[]> remove = useGlobalContext ? MaskedKeysContext.Remove : store.Remove;
        add([permanentKey]);
        try
        {
            Console.SetOut(writer);
            var monitor = new TestOptionsMonitor(new TjiddeLoggerOptions());
            using (var provider = new TjiddeLoggerProvider(monitor, new AsyncLocalCustomerContextAccessor(), accessor))
            {
                using var stop = new CancellationTokenSource();
                var churn = Task.Run(() =>
                {
                    var i = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        var key = churnKeys[i++ % churnKeys.Length];
                        add([key]);
                        remove([key]);
                    }
                });

                var loggers = Enumerable.Range(0, threads)
                    .Select(t => Task.Run(() =>
                    {
                        // Different categories, one shared masker
                        var logger = provider.CreateLogger($"My.App.Worker{t % 3}");
                        for (var n = 0; n < entriesPerThread; n++)
                        {
                            logger.LogInformation("value {PermanentKey} password=hunter2 entry {N}", permanentKey, n);
                            logger.LogInformation("{" + permanentKey + "} is sensitive", "structured-secret");
                        }
                    }))
                    .ToArray();

                try
                {
                    await Task.WhenAll(loggers);
                }
                catch
                {
                    errors.AddRange(loggers.Where(task => task.Exception is not null).SelectMany(task => task.Exception!.InnerExceptions));
                }
                finally
                {
                    stop.Cancel();
                    await churn;
                }
            }
        }
        finally
        {
            Console.SetOut(original);
            remove([permanentKey]);
            remove(churnKeys);
        }

        errors.Should().BeEmpty();

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(threads * entriesPerThread * 2);
        // The [Masked: ...] suffix names the masked property on purpose; the key must not appear anywhere else
        var bodies = lines.Select(line => line.Replace($"[Masked: {permanentKey}]", string.Empty)).ToArray();
        bodies.Should().NotContain(line => line.Contains(permanentKey, StringComparison.OrdinalIgnoreCase));
        lines.Count(line => line.EndsWith($"[REDACTED] is sensitive [Masked: {permanentKey}]")).Should().Be(threads * entriesPerThread);
        lines.Should().NotContain(line => line.Contains("hunter2"));
        lines.Should().NotContain(line => line.Contains("structured-secret"));
        lines.Should().NotContain(line => line.Contains("could not be rendered"));
    }

    [Fact]
    public void Masker_WithAccessorThatReusesOneMutableCollection_PicksUpChanges()
    {
        var accessor = new MutableKeysAccessor();
        var masker = new SensitiveDataMasker("[REDACTED]", additionalKeys: null, accessor);

        masker.IsSensitiveKey("customerCode").Should().BeFalse();

        accessor.Keys.Add("customerCode");
        masker.IsSensitiveKey("customerCode").Should().BeTrue();
        masker.MaskMessage("customerCode=12345").Should().NotContain("12345");

        accessor.Keys.Remove("customerCode");
        masker.IsSensitiveKey("customerCode").Should().BeFalse();
    }

    /// <summary>Returns the same mutable list on every call, unlike <see cref="MaskedKeysContext"/>'s snapshots.</summary>
    private sealed class MutableKeysAccessor : IMaskedKeysAccessor
    {
        public List<string> Keys { get; } = [];

        public IReadOnlyCollection<string> GetKeys() => Keys;
    }
}
