using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Services;
using Xunit;

namespace OnlineContract.Tests;

public class VariantAvailabilityServiceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public VariantAvailabilityServiceTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task No_Variant_Found_Returns_Error()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<VariantAvailabilityService>();
        var r = await svc.CheckVariantAvailabilityAsync(9999, "Purple", "XS", 1);
        Assert.False(r.IsAvailable);
        Assert.Contains("variant is not available", string.Join(';', r.Errors).ToLower());
    }

    [Fact]
    public async Task Insufficient_Stock_Returns_Error()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<VariantAvailabilityService>();
        // Product 100, variant Blue/L (id 102) has 0 qty in TestDataSeeder
        var r = await svc.CheckVariantAvailabilityAsync(100, "Blue", "L", 1);
        Assert.False(r.IsAvailable);
        Assert.True(r.AvailableQty >= 0);
    }

    [Fact]
    public async Task Sufficient_Stock_Succeeds_Across_Stores()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<VariantAvailabilityService>();
        // Red/M (variant 101) has 5 + 3 across stores; request 7
        var r = await svc.CheckVariantAvailabilityAsync(100, "Red", "M", 7);
        Assert.True(r.IsAvailable);
        Assert.Equal(101, r.FoundVariantId);
        Assert.True(r.AvailableQty >= 7);
    }
}
