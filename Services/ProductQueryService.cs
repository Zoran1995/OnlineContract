using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Dtos;
using OnlineContract.Models;

namespace OnlineContract.Services;

/// <summary>
/// Query helpers for product cards and variant/availability lookups.
/// </summary>
public class ProductQueryService
{
    private readonly AppDbContext _db;

    public ProductQueryService(AppDbContext db) { _db = db; }

    /// <summary>
    /// Returns cards for active, non-deleted products with filtering/sorting support.
    /// </summary>
    public async Task<List<ProductCardDto>> GetProductCardsAsync(
        string? size = null,
        string? color = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        string? sortBy = null,
        CancellationToken ct = default)
    {
        var baseProducts = _db.Products.AsNoTracking()
            .Where(p => p.Id > 0 && p.IsActive && !p.IsDeleted);

        // Build variant filter
        var variantQuery = _db.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && !v.IsDeleted);

        if (!string.IsNullOrWhiteSpace(size))
            variantQuery = variantQuery.Where(v => v.Size == size);

        if (!string.IsNullOrWhiteSpace(color))
            variantQuery = variantQuery.Where(v => v.Color == color);

        // If size or color filter is applied, only include products that have matching variants
        if (!string.IsNullOrWhiteSpace(size) || !string.IsNullOrWhiteSpace(color))
        {
            var matchingProductIds = variantQuery.Select(v => v.ProductId).Distinct();
            baseProducts = baseProducts.Where(p => matchingProductIds.Contains(p.Id));
        }

        var cards = await (
            from p in baseProducts
            let firstPhoto = (
                from v in _db.ProductVariants.AsNoTracking()
                where v.ProductId == p.Id && v.IsActive && !v.IsDeleted && !string.IsNullOrEmpty(v.PhotoFileName)
                orderby v.Id
                select v.PhotoFileName
            ).FirstOrDefault()
            let mainNote = (
                from n in _db.Notes.AsNoTracking()
                where n.ProductId == p.Id && n.IsMain && n.IsActive && !n.IsDeleted
                orderby n.InputDt descending
                select n.Comment
            ).FirstOrDefault()
            let minAmount = (
                from v in _db.ProductVariants.AsNoTracking()
                where v.ProductId == p.Id && v.IsActive && !v.IsDeleted
                select (decimal?)v.Amount
            ).Min() ?? 0m
            select new ProductCardDto
            {
                ProductId = p.Id,
                ProductName = p.Name ?? string.Empty,
                PhotoFileName = firstPhoto,
                MainComment = mainNote,
                MinAmount = minAmount,
                InputDt = p.InputDt
            }
        ).ToListAsync(ct);

        // Apply price filter in memory (after aggregation)
        if (minPrice.HasValue)
            cards = cards.Where(c => c.MinAmount >= minPrice.Value).ToList();
        if (maxPrice.HasValue)
            cards = cards.Where(c => c.MinAmount <= maxPrice.Value).ToList();

        // Apply sorting
        cards = sortBy?.ToLowerInvariant() switch
        {
            "price_asc" => cards.OrderBy(c => c.MinAmount).ToList(),
            "price_desc" => cards.OrderByDescending(c => c.MinAmount).ToList(),
            "newest" => cards.OrderByDescending(c => c.InputDt).ToList(),
            "oldest" => cards.OrderBy(c => c.InputDt).ToList(),
            _ => cards.OrderByDescending(c => c.InputDt).ToList() // default: newest first
        };

        return cards;
    }

    /// <summary>
    /// Returns all distinct sizes and colors across all active products for filter dropdowns.
    /// </summary>
    public async Task<(string[] Sizes, string[] Colors, decimal MinPrice, decimal MaxPrice)> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var activeVariants = _db.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && !v.IsDeleted);

        var sizes = await activeVariants
            .Where(v => !string.IsNullOrWhiteSpace(v.Size))
            .Select(v => v.Size!)
            .Distinct()
            .OrderBy(s => s)
            .ToArrayAsync(ct);

        var colors = await activeVariants
            .Where(v => !string.IsNullOrWhiteSpace(v.Color))
            .Select(v => v.Color!)
            .Distinct()
            .OrderBy(c => c)
            .ToArrayAsync(ct);

        var prices = await activeVariants
            .Select(v => v.Amount)
            .ToListAsync(ct);

        var minPrice = prices.Count > 0 ? prices.Min() : 0m;
        var maxPrice = prices.Count > 0 ? prices.Max() : 10000m;

        return (sizes, colors, minPrice, maxPrice);
    }

    /// <summary>
    /// Distinct sizes and colors for active, non-deleted variants of a product.
    /// </summary>
    public async Task<VariantsDto> GetDistinctSizesColorsAsync(int productId, CancellationToken ct = default)
    {
        var sizes = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == productId && v.IsActive && !v.IsDeleted && !string.IsNullOrWhiteSpace(v.Size))
            .Select(v => v.Size!)
            .Distinct()
            .OrderBy(s => s)
            .ToArrayAsync(ct);

        var colors = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == productId && v.IsActive && !v.IsDeleted && !string.IsNullOrWhiteSpace(v.Color))
            .Select(v => v.Color!)
            .Distinct()
            .OrderBy(c => c)
            .ToArrayAsync(ct);

        return new VariantsDto { Sizes = sizes, Colors = colors };
    }

    /// <summary>
    /// Resolve exact variant by product + size + color.
    /// </summary>
    public async Task<VariantSelectionDto?> GetVariantBySelectionAsync(int productId, string size, string color, CancellationToken ct = default)
    {
        size = (size ?? "").Trim();
        color = (color ?? "").Trim();
        if (string.IsNullOrEmpty(size) || string.IsNullOrEmpty(color)) return null;

        var v = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == productId && v.IsActive && !v.IsDeleted && (v.Size ?? "") == size && (v.Color ?? "") == color)
            .OrderBy(v => v.Id)
            .FirstOrDefaultAsync(ct);
        if (v == null) return null;
        return new VariantSelectionDto
        {
            ProductVariantId = v.Id,
            Amount = v.Amount,
            PhotoFileName = v.PhotoFileName,
            Size = v.Size,
            Color = v.Color
        };
    }

    /// <summary>
    /// Availability across stores for a given variant (qty_on_hand > 0, max 2).
    /// </summary>
    public async Task<List<AvailabilityDto>> GetAvailabilityAsync(int variantId, CancellationToken ct = default)
    {
        var q =
            from i in _db.ProductInventories.AsNoTracking()
            join s in _db.Stores.AsNoTracking() on i.StoreId equals s.StoreId
            where i.ProductVariantId == variantId && !i.IsDeleted && i.IsActive && i.QtyOnHand > 0
            orderby s.Name
            select new AvailabilityDto { StoreId = i.StoreId, StoreName = s.Name ?? string.Empty, QtyOnHand = i.QtyOnHand };

        return await q.Take(2).ToListAsync(ct);
    }
}
