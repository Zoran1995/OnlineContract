using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class EditabilityByStateTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public EditabilityByStateTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task NonDraft_Disables_Delete_Save_Submit()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 780, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Submitted, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 781, ContractId = 780, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Submitted, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var del = await client.PostAsync("/api/contracts/780/items/781/delete", null);
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
        var save = await client.PostAsJsonAsync("/api/contracts/780/save", new { contractStamp = 0 });
        Assert.Equal(HttpStatusCode.Conflict, save.StatusCode);
        var submitCod = await client.PostAsync("/api/contracts/780/submit?method=cod", null);
        Assert.Equal(HttpStatusCode.Conflict, submitCod.StatusCode);
        var submitOnline = await client.PostAsync("/api/contracts/780/submit?method=online", null);
        Assert.Equal(HttpStatusCode.Conflict, submitOnline.StatusCode);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}
