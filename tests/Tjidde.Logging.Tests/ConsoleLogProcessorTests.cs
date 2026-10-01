using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Logging;
using Xunit;

namespace Tjidde.Logging.Tests;

[Collection("ConsoleOutput")]
public sealed class ConsoleLogProcessorTests
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

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

    private static string[] Lines(string output)
        => output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Flush_WritesAllQueuedLinesInOrder_EvenBeyondQueueCapacity()
    {
        using var processor = new ConsoleLogProcessor();

        var output = CaptureConsoleOutput(() =>
        {
            for (var i = 0; i < 5000; i++)
                processor.Enqueue($"line {i}", color: null);

            processor.Flush(FlushTimeout).Should().BeTrue();
        });

        Lines(output).Should().Equal(Enumerable.Range(0, 5000).Select(i => $"line {i}"));
    }

    [Fact]
    public void Dispose_FlushesPendingLines()
    {
        var processor = new ConsoleLogProcessor();

        var output = CaptureConsoleOutput(() =>
        {
            for (var i = 0; i < 1000; i++)
                processor.Enqueue($"line {i}", color: null);

            processor.Dispose();
        });

        Lines(output).Should().HaveCount(1000);
    }

    [Fact]
    public void Enqueue_AfterDispose_WritesSynchronously()
    {
        var processor = new ConsoleLogProcessor();
        processor.Dispose();

        var output = CaptureConsoleOutput(() => processor.Enqueue("late line", color: null));

        output.Should().Contain("late line");
    }

    [Fact]
    public void Enqueue_KeepsWorking_WhenOutputWriterThrows()
    {
        using var processor = new ConsoleLogProcessor();
        var disposedWriter = new StringWriter();
        disposedWriter.Dispose();

        var original = Console.Out;
        Console.SetOut(disposedWriter);
        try
        {
            processor.Enqueue("lost line", color: null);
            processor.Flush(FlushTimeout).Should().BeTrue();
        }
        finally
        {
            Console.SetOut(original);
        }

        var output = CaptureConsoleOutput(() =>
        {
            processor.Enqueue("next line", color: null);
            processor.Flush(FlushTimeout).Should().BeTrue();
        });

        output.Should().Contain("next line");
    }

    [Fact]
    public void DisposingServiceProvider_FlushesQueuedLogs()
    {
        var output = CaptureConsoleOutput(() =>
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddTjiddeLogger());
            using var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILogger<ConsoleLogProcessorTests>>();

            for (var i = 0; i < 500; i++)
                logger.LogInformation("Message {Index}", i);
        });

        var lines = Lines(output);
        lines.Should().HaveCount(500);
        lines[^1].Should().EndWith("Message 499");
    }
}
