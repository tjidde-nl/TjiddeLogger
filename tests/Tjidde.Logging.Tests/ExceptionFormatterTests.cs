using FluentAssertions;
using Tjidde.Logging.Formatting;
using Xunit;

namespace Tjidde.Logging.Tests;

public sealed class ExceptionFormatterTests
{
    private readonly ExceptionFormatter _formatter = new();

    [Fact]
    public void Format_IncludesExceptionType()
    {
        var ex = new InvalidOperationException("Something went wrong");
        var result = _formatter.Format(ex);
        result.Should().Contain("System.InvalidOperationException");
    }

    [Fact]
    public void Format_IncludesExceptionMessage()
    {
        var ex = new InvalidOperationException("Something went wrong");
        var result = _formatter.Format(ex);
        result.Should().Contain("Something went wrong");
    }

    [Fact]
    public void Format_IncludesInnerException()
    {
        var inner = new ArgumentNullException("myParam", "Param was null");
        var outer = new InvalidOperationException("Outer error", inner);

        var result = _formatter.Format(outer);

        result.Should().Contain("System.InvalidOperationException");
        result.Should().Contain("System.ArgumentNullException");
        result.Should().Contain("Param was null");
    }

    [Fact]
    public void Format_IncludesInnerExceptionIndentation()
    {
        var inner = new Exception("Inner");
        var outer = new Exception("Outer", inner);

        var result = _formatter.Format(outer);

        result.Should().Contain("->");
    }

    [Fact]
    public void Format_WithStackTrace_IncludesStackTrace()
    {
        Exception ex;
        try
        {
            ThrowHelper();
            ex = new Exception("unreachable");
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        var formatter = new ExceptionFormatter(includeStackTrace: true);
        var result = formatter.Format(ex);

        result.Should().Contain("StackTrace");
    }

    [Fact]
    public void Format_WithoutStackTrace_OmitsStackTrace()
    {
        Exception ex;
        try
        {
            ThrowHelper();
            ex = new Exception("unreachable");
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        var formatter = new ExceptionFormatter(includeStackTrace: false);
        var result = formatter.Format(ex);

        result.Should().NotContain("StackTrace");
    }

    [Fact]
    public void Format_WithoutInnerExceptions_OmitsInnerException()
    {
        var inner = new Exception("Inner");
        var outer = new Exception("Outer", inner);

        var formatter = new ExceptionFormatter(includeStackTrace: false, includeInnerExceptions: false);
        var result = formatter.Format(outer);

        result.Should().NotContain("Inner");
        result.Should().Contain("Outer");
    }

    [Fact]
    public void Format_ThrowsArgumentNullException_ForNullException()
    {
        var act = () => _formatter.Format(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Format_HandlesDeepInnerExceptionChain()
    {
        var level3 = new Exception("Level 3");
        var level2 = new Exception("Level 2", level3);
        var level1 = new Exception("Level 1", level2);

        var result = _formatter.Format(level1);

        result.Should().Contain("Level 1");
        result.Should().Contain("Level 2");
        result.Should().Contain("Level 3");
    }

    private static void ThrowHelper() =>
        throw new InvalidOperationException("Test exception with stack trace");
}
