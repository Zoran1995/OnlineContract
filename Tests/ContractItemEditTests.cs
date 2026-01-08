using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class ContractItemEditTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractItemEditTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Edit_Item_Valid_Variant_And_Stock_Updates_Line_And_Header()
    {
        // Arrange: create a Draft contract for user 123 with one item
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 600, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 601, ContractId = 600, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }

        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        // Load header and items to get stamps
        var h = await client.GetFromJsonAsync<HeaderDto>("/api/contracts/600");
        Assert.NotNull(h);
        var list = await client.GetFromJsonAsync<ListDto>("/api/contracts/600/items?page=1&pageSize=10");
        Assert.NotNull(list);
        var row = list!.items.Single(x => x.id == 601);

        // Act: edit quantity to 2, keep same variant (has stock), change color/size to same values
        var payload = new {
            itemId = 601,
            quantity = 2,
            size = "M",
            color = "Red",
            itemStamp = row.stamp,
            contractStamp = h!.stamp
        };
        var resp = await client.PostAsJsonAsync("/api/contracts/600/items/601/edit", payload);
        resp.EnsureSuccessStatusCode();
        var okDto = await resp.Content.ReadFromJsonAsync<OkDto>();
        Assert.NotNull(okDto);
        Assert.True(okDto!.success);

        // Assert persisted changes
        using (var scope2 = _factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var det = await db.ContractDets.AsNoTracking().SingleAsync(x => x.Id == 601);
            var contract = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == 600);
            Assert.Equal(2, det.Quantity);
            Assert.Equal(1500m * 2, contract.Amount);
            Assert.True(det.Stamp > row.stamp);
            Assert.True(contract.Stamp > h!.stamp);
        }
    }

    [Fact]
    public async Task Edit_Item_Insufficient_Stock_Returns_Validation_Error()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 610, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 611, ContractId = 610, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }

        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var h = await client.GetFromJsonAsync<HeaderDto>("/api/contracts/610");
        var list = await client.GetFromJsonAsync<ListDto>("/api/contracts/610/items?page=1&pageSize=10");
        var row = list!.items.Single(x => x.id == 611);

        // Attempt to switch to variant 102 (Blue/L) which has 0 stock in seeder
        var payload = new {
            itemId = 611,
            quantity = 1,
            size = "L",
            color = "Blue",
            itemStamp = row.stamp,
            contractStamp = h!.stamp
        };
        var resp = await client.PostAsJsonAsync("/api/contracts/610/items/611/edit", payload);
        // Expect 200 with success=false and validation message (StableJson in tests)
        var dto = await resp.Content.ReadFromJsonAsync<OkDto>();
        Assert.NotNull(dto);
        Assert.False(dto!.success);
        Assert.Contains("Insufficient stock", dto.message ?? string.Empty);
    }

    [Fact]
    public async Task Edit_Item_Concurrency_Conflict_Returns_409()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 620, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 1 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 621, ContractId = 620, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 2, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }

        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var payload = new {
            itemId = 621,
            quantity = 2,
            size = "M",
            color = "Red",
            itemStamp = 1, // wrong item stamp
            contractStamp = 1
        };
        var resp = await client.PostAsJsonAsync("/api/contracts/620/items/621/edit", payload);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }

}

file sealed class ListDto { public List<ItemDto> items { get; set; } = new(); }
file sealed class ItemDto { public int id { get; set; } public int stamp { get; set; } }
file sealed class HeaderDto { public int stamp { get; set; } }
file sealed class OkDto { public bool success { get; set; } public string? message { get; set; } }
