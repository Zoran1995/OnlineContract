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
    /// Returns cards for active, non-deleted products with first variant photo, main note, and min price.
    /// </summary>
    public async Task<List<ProductCardDto>> GetProductCardsAsync(CancellationToken ct = default)
    {
        var baseProducts = _db.Products.AsNoTracking()
            .Where(p => p.Id > 0 && p.IsActive && !p.IsDeleted);

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
                MinAmount = minAmount
            }
        ).ToListAsync(ct);

        return cards;
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
