using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using OnlineContract.Services;
using Xunit;

namespace OnlineContract.Tests;

public class WspayStubTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public WspayStubTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Stub_CreateIntent_Returns_LocalRedirect_And_Persists_Payment()
    {
        var sp = _factory.Services;
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var svc = new PaymentService(db, new StubWspayClient(cfg), cfg);

        // Prepare a draft contract with one item
        var c = new Contract { Id = 9000, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, IsActive = true, IsDeleted = false, Stamp = 0, Amount = 0m };
        db.Contracts.Add(c);
        db.ContractDets.Add(new ContractDet { Id = 9001, ContractId = 9000, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "X", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 });
        await db.SaveChangesAsync();

        var info = new CustomerInfo("test@example.com", "Test User");
        var (ok, url, ext, err) = await svc.CreatePaymentIntentAsync(9000, 1500m, info);
        Assert.True(ok);
        Assert.NotNull(url);
        Assert.Contains("/payments/wspay/mock?", url);
        Assert.False(string.IsNullOrWhiteSpace(ext));
        Assert.Null(err);

        var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.ExternalOrderId == ext);
        Assert.NotNull(p);
        Assert.Equal("Pending", p!.Status);
        Assert.Equal(9000, p.ContractId);
    }

    [Fact]
    public async Task Stub_Callback_Success_Submits_Contract_And_Payment_Succeeds()
    {
        var sp = _factory.Services;
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var svc = new PaymentService(db, new StubWspayClient(cfg), cfg);

        var c = new Contract { Id = 9100, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, IsActive = true, IsDeleted = false, Stamp = 0, Amount = 0m };
        db.Contracts.Add(c);
        db.ContractDets.Add(new ContractDet { Id = 9101, ContractId = 9100, ProductVariantId = 101, Quantity = 2, Amount = 1500m, ProductName = "Y", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 });
        await db.SaveChangesAsync();

        var info = new CustomerInfo("test@example.com", "Test User");
        var (_, _, ext, _) = await svc.CreatePaymentIntentAsync(9100, 3000m, info);
        var res = await svc.HandleCallbackAsync(new Dictionary<string,string> { { "status", "success" }, { "external_order_id", ext } });
        Assert.True(res.Success);

        var p = await db.Payments.FirstAsync(x => x.ExternalOrderId == ext);
        var c2 = await db.Contracts.FirstAsync(x => x.Id == 9100);
        Assert.Equal("Succeeded", p.Status);
        Assert.Equal(OnlineContract.Helpers.ContractState.Submitted, c2.ContractState);
        Assert.Equal(c2.Amount, c2.AmtMatched);
    }

    [Fact]
    public async Task Stub_Callback_Failure_Does_Not_Submit_Contract()
    {
        var sp = _factory.Services;
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var svc = new PaymentService(db, new StubWspayClient(cfg), cfg);

        var c = new Contract { Id = 9200, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, IsActive = true, IsDeleted = false, Stamp = 0, Amount = 0m };
        db.Contracts.Add(c);
        db.ContractDets.Add(new ContractDet { Id = 9201, ContractId = 9200, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "Z", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = DateTime.Now, InputUserId = 123, IsActive = true, IsDeleted = false, Stamp = 0 });
        await db.SaveChangesAsync();

        var info = new CustomerInfo("test@example.com", "Test User");
        var (_, _, ext, _) = await svc.CreatePaymentIntentAsync(9200, 1500m, info);
        var res = await svc.HandleCallbackAsync(new Dictionary<string,string> { { "status", "failure" }, { "external_order_id", ext } });
        Assert.False(res.Success);

        var p = await db.Payments.FirstAsync(x => x.ExternalOrderId == ext);
        var c2 = await db.Contracts.FirstAsync(x => x.Id == 9200);
        Assert.Equal("Failed", p.Status);
        Assert.Equal(OnlineContract.Helpers.ContractState.Draft, c2.ContractState);
    }
}
