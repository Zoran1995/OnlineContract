using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using Xunit;

namespace OnlineContract.Tests;

public class ContractsFiltersTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractsFiltersTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async System.Threading.Tasks.Task Name_Filter_Is_Contains_CaseInsensitive()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);

        var resp = await client.GetAsync("/api/contracts?page=1&pageSize=10&name=user");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<ContractsResponse>();
        Assert.NotNull(result);
        Assert.True(result!.totalCount >= 1);
        Assert.Contains(result.items.Select(i => i.customerFullName ?? string.Empty), n => (n ?? string.Empty).ToLower().Contains("user"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Allocation_Match_Data_Present()
    {
        // Create a contract with amtMatched == amount
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = new OnlineContract.Models.Contract { Id = 9600, InputUserId = 123, ContractState = ContractState.Draft, IsActive = true, IsDeleted = false, Amount = 0m, Stamp = 0 };
        db.Contracts.Add(c);
        db.ContractDets.Add(new OnlineContract.Models.ContractDet { Id = 9601, ContractId = 9600, ProductVariantId = 101, Quantity = 2, Amount = 1500m, ProductName = "X", Size = "M", Color = "Red", ItemStateId = ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 });
        await db.SaveChangesAsync();
        // Recompute header
        var amt = await db.ContractDets.AsNoTracking().Where(d => d.ContractId == 9600).Select(d => (decimal?)d.AmtGross).SumAsync() ?? 0m;
        c.Amount = amt;
        c.AmtMatched = amt;
        await db.SaveChangesAsync();

        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var resp = await client.GetAsync("/api/contracts?page=1&pageSize=10&name=Test");
        var result = await resp.Content.ReadFromJsonAsync<ContractsResponse>();
        Assert.NotNull(result);
        Assert.True(result!.items.Any());
        // Ensure at least one row has amtMatched == amount
        Assert.Contains(result.items, x => (x.amtMatched ?? 0m) == (x.amount ?? 0m));
    }

    private sealed class ContractsResponse
    {
        public List<ContractRow> items { get; set; } = new();
        public int totalCount { get; set; }
        public int totalPages { get; set; }
        public string? sortBy { get; set; }
        public string? sortDir { get; set; }
    }

    private sealed class ContractRow
    {
        public int id { get; set; }
        public decimal? amount { get; set; }
        public decimal? amtMatched { get; set; }
        public string? customerFullName { get; set; }
    }
}
