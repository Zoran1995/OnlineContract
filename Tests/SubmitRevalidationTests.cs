using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class SubmitRevalidationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SubmitRevalidationTests(WebAppFactory factory) { _factory = factory; }

    [Theory]
    [InlineData("cod")]
    [InlineData("online")]
    public async Task Submit_Fails_When_Insufficient_Stock(string method)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Use distinct IDs per test case to avoid collisions across theory runs
            var contractId = string.Equals(method, "cod", StringComparison.OrdinalIgnoreCase) ? 790 : 792;
            var detId = contractId + 1;
            var contract = new Contract { Id = contractId, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = detId, ContractId = contractId, ProductVariantId = 101, Quantity = 999, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            await db.SaveChangesAsync();
        }
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        var submitId = string.Equals(method, "cod", StringComparison.OrdinalIgnoreCase) ? 790 : 792;
        var resp = await client.PostAsync($"/api/contracts/{submitId}/submit?method={method}", null);
        var dto = await resp.Content.ReadFromJsonAsync<SubmitDto>();
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

file sealed class SubmitDto { public bool success { get; set; } public string? message { get; set; } }
