using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class ContractDeleteRulesTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractDeleteRulesTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Deleting_Item_SoftDeletes_Detail_Recomputes_Header_And_No_Check_Violation()
    {
        int contractId = 880;
        int det1 = 881; int det2 = 882;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = new Contract { Id = contractId, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(c);
            var d1 = new ContractDet { Id = det1, ContractId = contractId, ProductVariantId = 101, Quantity = 2, Amount = 1000m, ProductName = "Item1", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 };
            var d2 = new ContractDet { Id = det2, ContractId = contractId, ProductVariantId = 102, Quantity = 1, Amount = 500m, ProductName = "Item2", Size = "L", Color = "Blue", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.ContractDets.AddRange(d1, d2);
            await db.SaveChangesAsync();
            // set header to some value; recompute on delete will use sum of AmtGross
            decimal gross1 = d1.Amount * d1.Quantity; // AmtGross (no tax)
            decimal gross2 = d2.Amount * d2.Quantity; // AmtGross (no tax)
            c.Amount = gross1 + gross2;
            await db.SaveChangesAsync();
        }

        var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(cookie)) client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var resp = await client.PostAsync($"/api/contracts/{contractId}/items/{det2}/delete", null);
        resp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == contractId);
            var d1 = await db.ContractDets.AsNoTracking().SingleAsync(x => x.Id == det1);
            var d2 = await db.ContractDets.AsNoTracking().SingleAsync(x => x.Id == det2);
            Assert.True(d2.IsDeleted);
            Assert.False(d2.IsActive);
            // header amount reduced to sum of remaining lines' AmtGross
            decimal gross1 = d1.Amount * d1.Quantity;
            Assert.Equal(gross1, c.Amount);
        }
    }

    [Fact]
    public async Task Deleting_Last_Item_SoftDeletes_Header()
    {
        int contractId = 890;
        int det1 = 891;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = new Contract { Id = contractId, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(c);
            var d1 = new ContractDet { Id = det1, ContractId = contractId, ProductVariantId = 101, Quantity = 1, Amount = 100m, ProductName = "Only", Size = "S", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.ContractDets.Add(d1);
            await db.SaveChangesAsync();
            c.Amount = Math.Round(d1.Amount * d1.Quantity * 1.20m, 2, MidpointRounding.AwayFromZero);
            await db.SaveChangesAsync();
        }

        var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(cookie)) client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var resp = await client.PostAsync($"/api/contracts/{contractId}/items/{det1}/delete", null);
        resp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == contractId);
            Assert.True(c.IsDeleted);
            Assert.False(c.IsActive);
        }
    }
}
