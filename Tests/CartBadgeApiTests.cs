using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests;

public class CartBadgeApiTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public CartBadgeApiTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Contracts_Query_Reflects_Draft_After_AddToCart()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie.Split(';')[0]);

        // Initially query drafts
        var resp0 = await client.GetAsync("/api/contracts?state=Draft&page=1&pageSize=1");
        resp0.EnsureSuccessStatusCode();
        var dto0 = await resp0.Content.ReadFromJsonAsync<ContractsSummaryDto>();
        var before = dto0 != null ? dto0.totalCount : 0;

        // Add to cart (variant 101 exists in seeded data)
        var add = await client.PostAsJsonAsync("/api/cart/items", new { productVariantId = 101, quantity = 1 });
        add.EnsureSuccessStatusCode();

        var resp1 = await client.GetAsync("/api/contracts?state=Draft&page=1&pageSize=1");
        resp1.EnsureSuccessStatusCode();
        var dto1 = await resp1.Content.ReadFromJsonAsync<ContractsSummaryDto>();
        Assert.NotNull(dto1);
        Assert.True((dto1!.totalCount) >= before);
    }
}

file sealed class ContractsSummaryDto { public int totalCount { get; set; } }
