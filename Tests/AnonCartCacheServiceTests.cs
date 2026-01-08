using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Services;
using OnlineContract.Data;
using Xunit;

namespace OnlineContract.Tests;

public class AnonCartCacheServiceTests
{
    private (AnonCartCacheService svc, AppDbContext db, IHttpContextAccessor http) Create()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        TestDataSeeder.Seed(db);
        var http = sp.GetRequiredService<IHttpContextAccessor>();
        http.HttpContext = new DefaultHttpContext();
        http.HttpContext.Request.Scheme = "http"; // dev-friendly
        var cache = sp.GetRequiredService<IDistributedCache>();
        var logger = sp.GetRequiredService<ILogger<AnonCartCacheService>>();
        var svc = new AnonCartCacheService(cache, http, db, logger);
        return (svc, db, http);
    }

    [Fact]
    public async Task GetOrCreateAnonId_SetsCookie_And_UpsertStoresItem()
    {
        var (svc, db, http) = Create();
        var id = svc.GetOrCreateAnonId();
        Assert.NotNull(http.HttpContext);
        Assert.True(http.HttpContext!.Response.Headers.ContainsKey("Set-Cookie"));
        await svc.UpsertAsync(id, 101, 2, default);
        var items = await svc.GetAsync(id, default);
        Assert.Single(items);
        Assert.Equal(101, items[0].VariantId);
        Assert.Equal(2, items[0].Qty);
    }

    [Fact]
    public async Task Upsert_Rejects_OverAvailability()
    {
        var (svc, db, _) = Create();
        var id = svc.GetOrCreateAnonId();
        // Total available for 101 is 8
        await svc.UpsertAsync(id, 101, 7, default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await svc.UpsertAsync(id, 101, 2, default));
    }

    [Fact]
    public async Task Upsert_Allows_ZeroAvailability()
    {
        var (svc, db, _) = Create();
        var id = svc.GetOrCreateAnonId();
        await svc.UpsertAsync(id, 102, 3, default);
        var items = await svc.GetAsync(id, default);
        Assert.Single(items);
        Assert.Equal(102, items[0].VariantId);
        Assert.Equal(3, items[0].Qty);
    }

    [Fact]
    public async Task Clear_RemovesCache_And_Cookie()
    {
        var (svc, db, http) = Create();
        var id = svc.GetOrCreateAnonId();
        await svc.UpsertAsync(id, 101, 1, default);
        await svc.ClearAsync(id, default);
        var items = await svc.GetAsync(id, default);
        Assert.Empty(items);
        // Cookie deletion writes Set-Cookie header
        Assert.NotNull(http.HttpContext);
        Assert.True(http.HttpContext!.Response.Headers.ContainsKey("Set-Cookie"));
    }
}
