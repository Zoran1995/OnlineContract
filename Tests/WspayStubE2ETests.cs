using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using Xunit;

namespace OnlineContract.Tests;

public class WspayStubE2ETests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public WspayStubE2ETests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async System.Threading.Tasks.Task Online_Submit_Success_Sets_Submitted()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Prepare a draft contract
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = new OnlineContract.Models.Contract { Id = 9300, InputUserId = 123, ContractState = ContractState.Draft, IsActive = true, IsDeleted = false, Stamp = 0, Amount = 0m };
        db.Contracts.Add(c);
        db.ContractDets.Add(new OnlineContract.Models.ContractDet { Id = 9301, ContractId = 9300, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "A", Size = "M", Color = "Red", ItemStateId = ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 });
        await db.SaveChangesAsync();

        var resp = await client.PostAsync($"/api/contracts/{9300}/submit?method=online", new StringContent(""));
        var result = await resp.Content.ReadFromJsonAsync<SubmitResult>();
        Assert.True(result!.success);
        Assert.NotNull(result.redirectUrl);
        Assert.NotNull(result.reference);

        var cb = await client.PostAsJsonAsync("/api/payments/wspay/mock-callback", new { reference = result.reference, status = "Succeeded", contractId = 9300 });
        Assert.True(cb.IsSuccessStatusCode);

        var c2 = await db.Contracts.AsNoTracking().FirstAsync(x => x.Id == 9300);
        Assert.Equal(ContractState.Submitted, c2.ContractState);
    }

    [Fact]
    public async System.Threading.Tasks.Task Online_Submit_Failure_Stays_Draft()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = new OnlineContract.Models.Contract { Id = 9400, InputUserId = 123, ContractState = ContractState.Draft, IsActive = true, IsDeleted = false, Stamp = 0, Amount = 0m };
        db.Contracts.Add(c);
        db.ContractDets.Add(new OnlineContract.Models.ContractDet { Id = 9401, ContractId = 9400, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "B", Size = "M", Color = "Red", ItemStateId = ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 });
        await db.SaveChangesAsync();

        var resp = await client.PostAsync($"/api/contracts/{9400}/submit?method=online", new StringContent(""));
        var result = await resp.Content.ReadFromJsonAsync<SubmitResult>();
        Assert.True(result!.success);

        var cb = await client.PostAsJsonAsync("/api/payments/wspay/mock-callback", new { reference = result.reference, status = "Failed", contractId = 9400 });
        Assert.True(cb.IsSuccessStatusCode);

        var c2 = await db.Contracts.AsNoTracking().FirstAsync(x => x.Id == 9400);
        Assert.Equal(ContractState.Draft, c2.ContractState);
    }

    private sealed class SubmitResult
    {
        public bool success { get; set; }
        public string? redirectUrl { get; set; }
        public string? reference { get; set; }
    }
}
