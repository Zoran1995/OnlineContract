using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using Xunit;

namespace OnlineContract.Tests;

public class ApiBehaviorTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ApiBehaviorTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Unauthenticated_CartItems_Returns401()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.PostAsJsonAsync("/api/cart/items", new { ProductVariantId = 101, Quantity = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_AddToCart_Works_And_Summary_ReturnsItems()
    {
        var client = _factory.CreateClientNoRedirect();
        var add = await client.PostAsJsonAsync("/api/anon-cart/items", new { ProductVariantId = 101, Quantity = 2 });
        Assert.True(add.IsSuccessStatusCode);
        var setCookie = add.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.FirstOrDefault() : null;
        // Propagate cookie manually
        if (!string.IsNullOrEmpty(setCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(setCookie, "anon_cart_id"));
        var sum = await client.GetAsync("/api/anon-cart/summary");
        Assert.True(sum.IsSuccessStatusCode);
        var data = await sum.Content.ReadFromJsonAsync<AnonSummaryDto>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.itemCount);
    }

    [Fact]
    public async Task Checkout_Unauthenticated_Redirects_To_Login()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/login?returnUrl=%2Fcheckout", resp.Headers.Location!.ToString());
    }
    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        // naive parsing: find name=value at start
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}

file sealed class AnonSummaryDto
{
    public int itemCount { get; set; }
    public List<AnonSummaryItem> items { get; set; } = new();
}

file sealed class AnonSummaryItem
{
    public int variantId { get; set; }
    public int qty { get; set; }
}
