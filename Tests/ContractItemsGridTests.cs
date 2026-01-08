using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OnlineContract.Tests;

public class ContractItemsGridTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractItemsGridTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Contract_Items_List_Returns_Active_NotDeleted_And_State_Text()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
        var resp = await client.GetAsync("/api/contracts/500/items?page=1&pageSize=10");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ContractItemsDto>();
        Assert.NotNull(dto);
        Assert.True(dto!.items.Count >= 2);
        foreach (var item in dto.items)
        {
            Assert.True(item.quantity > 0);
            Assert.True(item.amount > 0);
            Assert.False(string.IsNullOrEmpty(item.itemStateText));
        }
    }

    [Fact]
    public async Task Contract_Items_List_Reflects_Inactive_Status()
    {
        // Mark item 501 as inactive directly in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
        var det = await db.ContractDets.FirstOrDefaultAsync(x => x.Id == 501);
        Assert.NotNull(det);
        det!.IsActive = false;
        await db.SaveChangesAsync();

        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var resp = await client.GetAsync("/api/contracts/500/items?page=1&pageSize=10");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ContractItemsDto>();
        Assert.NotNull(dto);
        var row = dto!.items.FirstOrDefault(x => x.id == 501);
        Assert.NotNull(row);
        Assert.False(row!.isActive);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}

file sealed class ContractItemsDto
{
    public List<ContractItemRow> items { get; set; } = new();
    public int totalCount { get; set; }
    public int totalPages { get; set; }
}

file sealed class ContractItemRow
{
    public int id { get; set; }
    public string productName { get; set; } = string.Empty;
    public string size { get; set; } = string.Empty;
    public string color { get; set; } = string.Empty;
    public int quantity { get; set; }
    public decimal amount { get; set; }
    public decimal amtGross { get; set; }
    public int itemStateId { get; set; }
    public string itemStateText { get; set; } = string.Empty;
    public string inputDt { get; set; } = string.Empty;
    public bool isActive { get; set; }
    public string photoFileName { get; set; } = string.Empty;
}
