using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class OwnerOnlyAccessTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public OwnerOnlyAccessTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task NonOwner_Cannot_Access_Contract_Header_And_Items_Or_Mutate()
    {
        // Arrange: create a Draft contract owned by another user (999)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 710, InputUserId = 999, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 711, ContractId = 710, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 999, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            contract.Amount = det.Quantity * det.Amount;
            await db.SaveChangesAsync();
        }

        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        // Header
        var h = await client.GetAsync("/api/contracts/710");
        Assert.Equal(HttpStatusCode.Forbidden, h.StatusCode);
        // Items list
        var list = await client.GetAsync("/api/contracts/710/items?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        // Delete
        var del = await client.PostAsync("/api/contracts/710/items/711/delete", null);
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
        // Edit
        var edit = await client.PostAsJsonAsync("/api/contracts/710/items/711/edit", new { itemId = 711, quantity = 2, size = "M", color = "Red", itemStamp = 0, contractStamp = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        // Save
        var save = await client.PostAsJsonAsync("/api/contracts/710/save", new { contractStamp = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        // Submit
        var submit = await client.PostAsync("/api/contracts/710/submit?method=cod", null);
        Assert.Equal(HttpStatusCode.Forbidden, submit.StatusCode);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}
