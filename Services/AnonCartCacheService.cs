using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;

namespace OnlineContract.Services;

public class AnonCartCacheService
{
    public const string CookieName = "anon_cart_id";
    private readonly IDistributedCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppDbContext _db;
    private readonly ILogger<AnonCartCacheService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public sealed class AnonCartItemDto
    {
        public int VariantId { get; set; }
        public int Qty { get; set; }
    }

    public AnonCartCacheService(IDistributedCache cache, IHttpContextAccessor httpContextAccessor, AppDbContext db, ILogger<AnonCartCacheService> logger)
    {
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _logger = logger;
    }

    public HttpContext HttpContext => _httpContextAccessor.HttpContext!;

    public Guid GetOrCreateAnonId()
    {
        var ctx = HttpContext;
        if (ctx.Request.Cookies.TryGetValue(CookieName, out var raw) && Guid.TryParse(raw, out var gid))
        {
            return gid;
        }
        var id = Guid.NewGuid();
        SetAnonCookie(id);
        _logger.LogInformation("Anon cart id created: {AnonId}", id);
        return id;
    }

    public bool TryGetAnonId(out Guid anonId)
    {
        anonId = Guid.Empty;
        var ctx = HttpContext;
        if (ctx.Request.Cookies.TryGetValue(CookieName, out var raw) && Guid.TryParse(raw, out var gid))
        {
            anonId = gid;
            return true;
        }
        return false;
    }

    public async Task<List<AnonCartItemDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var key = CacheKey(id);
        var raw = await _cache.GetStringAsync(key, ct);
        if (string.IsNullOrEmpty(raw)) return new List<AnonCartItemDto>();
        var items = JsonSerializer.Deserialize<List<AnonCartItemDto>>(raw, _jsonOptions) ?? new List<AnonCartItemDto>();
        return items;
    }

    public async Task UpsertAsync(Guid id, int variantId, int qty, CancellationToken ct)
    {
        if (variantId <= 0) throw new InvalidOperationException("Invalid variant");
        if (qty < 1) throw new InvalidOperationException("Quantity must be >= 1");

        // Validate variant & product
        var variant = await _db.ProductVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == variantId && v.IsActive && !v.IsDeleted, ct);
        if (variant == null) throw new InvalidOperationException("Variant not found or inactive");
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == variant.ProductId && p.IsActive && !p.IsDeleted, ct);
        if (product == null) throw new InvalidOperationException("Product not found or inactive");

        // Current items from cache
        var items = await GetAsync(id, ct);
        var existing = items.FirstOrDefault(x => x.VariantId == variantId);
        var existingQty = existing == null ? 0 : existing.Qty;
        var targetQty = existingQty + qty;

        // Availability across active/not-deleted inventories
        var availableQty = await _db.ProductInventories.AsNoTracking()
            .Where(i => i.ProductVariantId == variantId && !i.IsDeleted && i.IsActive)
            .Select(i => (int?)i.QtyOnHand).SumAsync(ct) ?? 0;

        if (availableQty > 0 && targetQty > availableQty)
        {
            throw new InvalidOperationException("We don’t have enough items in stock. Please reduce the quantity and try again.");
        }

        // Upsert (store only variantId + qty; no price)
        var updated = items
            .Where(x => x.VariantId != variantId)
            .Select(x => new AnonCartItemDto { VariantId = x.VariantId, Qty = x.Qty })
            .ToList();
        updated.Add(new AnonCartItemDto { VariantId = variantId, Qty = targetQty });

        await SetAsync(id, updated, ct);
        _logger.LogInformation("Anon cart upserted: {AnonId} variant={VariantId} qty={Qty}", id, variantId, targetQty);
    }

    public async Task ClearAsync(Guid id, CancellationToken ct)
    {
        var key = CacheKey(id);
        await _cache.RemoveAsync(key, ct);
        DeleteAnonCookie();
        _logger.LogInformation("Anon cart cleared: {AnonId}", id);
    }

    private async Task SetAsync(Guid id, List<AnonCartItemDto> items, CancellationToken ct)
    {
        var key = CacheKey(id);
        var payload = JsonSerializer.Serialize(items, _jsonOptions);
        var opts = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
            SlidingExpiration = TimeSpan.FromDays(7)
        };
        await _cache.SetStringAsync(key, payload, opts, ct);
        // Ensure cookie exists
        SetAnonCookie(id);
    }

    private static string CacheKey(Guid id) => $"anon_cart:{id}";

    private void SetAnonCookie(Guid id)
    {
        var ctx = HttpContext;
        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            IsEssential = true
        };
        ctx.Response.Cookies.Append(CookieName, id.ToString(), opts);
    }

    private void DeleteAnonCookie()
    {
        var ctx = HttpContext;
        if (ctx.Request.Cookies.ContainsKey(CookieName))
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(-1),
                IsEssential = true
            };
            ctx.Response.Cookies.Append(CookieName, string.Empty, opts);
        }
    }
}
