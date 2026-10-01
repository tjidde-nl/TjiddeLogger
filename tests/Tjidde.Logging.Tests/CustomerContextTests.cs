using FluentAssertions;
using Tjidde.Logging.Context;
using Xunit;

namespace Tjidde.Logging.Tests;

public sealed class CustomerContextTests
{
    [Fact]
    public void CustomerContext_Set_ReturnsValue()
    {
        CustomerContext.Set("AcmeCorp");
        CustomerContext.Current.Should().Be("AcmeCorp");
        CustomerContext.Clear();
    }

    [Fact]
    public void CustomerContext_Clear_ReturnsNull()
    {
        CustomerContext.Set("AcmeCorp");
        CustomerContext.Clear();
        CustomerContext.Current.Should().BeNull();
    }

    [Fact]
    public void CustomerContext_DefaultIsNull()
    {
        CustomerContext.Clear();
        CustomerContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task CustomerContext_IsIsolatedPerAsyncFlow()
    {
        CustomerContext.Clear();

        string? contextInTask1 = null;
        string? contextInTask2 = null;

        var task1 = Task.Run(async () =>
        {
            CustomerContext.Set("CustomerA");
            await Task.Delay(20);
            contextInTask1 = CustomerContext.Current;
        });

        var task2 = Task.Run(async () =>
        {
            CustomerContext.Set("CustomerB");
            await Task.Delay(20);
            contextInTask2 = CustomerContext.Current;
        });

        await Task.WhenAll(task1, task2);

        contextInTask1.Should().Be("CustomerA");
        contextInTask2.Should().Be("CustomerB");
    }

    [Fact]
    public void AsyncLocalCustomerContextAccessor_ReturnsCurrentContext()
    {
        CustomerContext.Set("TenantX");
        var accessor = new AsyncLocalCustomerContextAccessor();
        accessor.GetCustomerContext().Should().Be("TenantX");
        CustomerContext.Clear();
    }

    [Fact]
    public void AsyncLocalCustomerContextAccessor_ReturnsNull_WhenNotSet()
    {
        CustomerContext.Clear();
        var accessor = new AsyncLocalCustomerContextAccessor();
        accessor.GetCustomerContext().Should().BeNull();
    }
}
