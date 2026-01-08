using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class SaveFlowTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SaveFlowTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Save_Revalidates_Stock_Recomputes_Header_Gross_No_State_Change()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 730, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det1 = new ContractDet { Id = 731, ContractId = 730, ProductVariantId = 101, Quantity = 2, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            var det2 = new ContractDet { Id = 732, ContractId = 730, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.AddRange(det1, det2);
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var header = await client.GetFromJsonAsync<HeaderDtoEx>("/api/contracts/730");
        Assert.NotNull(header);
        var resp = await client.PostAsJsonAsync("/api/contracts/730/save", new { contractStamp = header!.stamp });
        Assert.True(resp.IsSuccessStatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<SaveDto>();
        Assert.NotNull(dto);
        Assert.True(dto!.success);

        using (var scope2 = _factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == 730);
            // Header amount is sum of AmtGross (no tax): 1500*2=3000; 1500*1=1500; header=4500
            Assert.Equal(4500m, c.Amount);
            Assert.Equal(OnlineContract.Helpers.ContractState.Draft, c.ContractState);
        }
    }

    [Fact]
    public async Task Save_Fails_When_Insufficient_Stock()
    {
        // Arrange: set quantity beyond inventory (v1 total stock = 8)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 740, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det1 = new ContractDet { Id = 741, ContractId = 740, ProductVariantId = 101, Quantity = 9, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det1);
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
        var h = await client.GetFromJsonAsync<HeaderDtoEx>("/api/contracts/740");
        Assert.NotNull(h);
        var resp = await client.PostAsJsonAsync("/api/contracts/740/save", new { contractStamp = h!.stamp });
        var dto = await resp.Content.ReadFromJsonAsync<SaveDto>();
        Assert.NotNull(dto);
        Assert.False(dto!.success);
        Assert.Contains("Insufficient stock", dto!.message ?? string.Empty);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}

file sealed class HeaderDtoEx { public int stamp { get; set; } }
file sealed class SaveDto { public bool success { get; set; } public string? message { get; set; } }
