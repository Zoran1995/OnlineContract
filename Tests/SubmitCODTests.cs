using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class SubmitCODTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SubmitCODTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task COD_Submit_Sets_Submitted_And_AmtMatched_Zero()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 770, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 771, ContractId = 770, ProductVariantId = 101, Quantity = 2, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var resp = await client.PostAsync("/api/contracts/770/submit?method=cod", null);
        Assert.True(resp.IsSuccessStatusCode);

        using (var scope2 = _factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == 770);
            var items = await db.ContractDets.AsNoTracking().Where(x => x.ContractId == 770).ToListAsync();
            Assert.Equal(OnlineContract.Helpers.ContractState.Submitted, c.ContractState);
            Assert.Equal(0m, c.AmtMatched);
            Assert.All(items, it => Assert.Equal(OnlineContract.Helpers.ProductStateInOrder.Submitted, it.ItemStateId));
        }
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}
