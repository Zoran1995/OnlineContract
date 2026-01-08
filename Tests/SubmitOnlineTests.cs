using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class SubmitOnlineTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SubmitOnlineTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Online_Submit_Success_Via_Callback_Sets_Submitted_And_Matched_Idempotent()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 750, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 751, ContractId = 750, ProductVariantId = 101, Quantity = 2, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        // Initiate payment
        var resp = await client.PostAsync("/api/contracts/750/submit?method=online", null);
        Assert.True(resp.IsSuccessStatusCode);

        // Callback success
        var cb = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "success", contractId = 750 });
        var ok = await cb.Content.ReadFromJsonAsync<CallbackDto>();
        Assert.NotNull(ok);
        Assert.True(ok!.success);

        // Assert state and matched
        using (var scope2 = _factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == 750);
            var items = await db.ContractDets.AsNoTracking().Where(x => x.ContractId == 750).ToListAsync();
            Assert.Equal(OnlineContract.Helpers.ContractState.Submitted, c.ContractState);
            Assert.Equal(c.Amount, c.AmtMatched);
            Assert.All(items, it => Assert.Equal(OnlineContract.Helpers.ProductStateInOrder.Submitted, it.ItemStateId));
        }

        // Duplicate callback -> idempotent
        var cb2 = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "success", contractId = 750 });
        var ok2 = await cb2.Content.ReadFromJsonAsync<CallbackDto2>();
        Assert.NotNull(ok2);
        Assert.True(ok2!.success);
        Assert.True(ok2!.idempotent);
    }

    [Fact]
    public async Task Online_Submit_Failure_Leaves_Draft()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 760, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 761, ContractId = 760, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var resp = await client.PostAsync("/api/contracts/760/submit?method=online", null);
        Assert.True(resp.IsSuccessStatusCode);

        var cb = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "failure", contractId = 760 });
        var j = await cb.Content.ReadFromJsonAsync<CallbackDto>();
        Assert.NotNull(j);
        Assert.False(j!.success);

        using (var scope2 = _factory.Services.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == 760);
            Assert.Equal(OnlineContract.Helpers.ContractState.Draft, c.ContractState);
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

file sealed class CallbackDto { public bool success { get; set; } }
file sealed class CallbackDto2 { public bool success { get; set; } public bool idempotent { get; set; } }
