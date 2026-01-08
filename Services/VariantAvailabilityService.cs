using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;

namespace OnlineContract.Services;

public class VariantAvailabilityService
{
    private readonly AppDbContext _db;
    public VariantAvailabilityService(AppDbContext db) { _db = db; }

    public async Task<bool> CheckVariantAvailabilityAsync(int variantId, int requiredQty, CancellationToken ct = default)
    {
        if (variantId <= 0 || requiredQty <= 0) return false;
        // Variant must be active and not deleted
        var variantOk = await _db.ProductVariants.AsNoTracking()
            .AnyAsync(v => v.Id == variantId && v.IsActive && !v.IsDeleted, ct);
        if (!variantOk) return false;
        // Sum available qty across all active, non-deleted inventories
        var total = await _db.ProductInventories.AsNoTracking()
            .Where(i => i.ProductVariantId == variantId && i.IsActive && !i.IsDeleted)
            .Select(i => (int?)i.QtyOnHand).SumAsync(ct) ?? 0;
        return total >= requiredQty;
    }

    public sealed class VariantAvailabilityResult
    {
        public int FoundVariantId { get; set; }
        public int AvailableQty { get; set; }
        public bool IsAvailable { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public async Task<VariantAvailabilityResult> CheckVariantAvailabilityAsync(
        int productId,
        string color,
        string size,
        int requestedQty,
        int? preferredStoreId = null,
        CancellationToken ct = default)
    {
        var result = new VariantAvailabilityResult();
        if (productId <= 0) { result.Errors.Add("Invalid product."); return result; }
        if (requestedQty <= 0) { result.Errors.Add("Quantity must be greater than 0."); return result; }

        var colorNorm = (color ?? string.Empty).Trim();
        var sizeNorm = (size ?? string.Empty).Trim();
        var colorLower = colorNorm.ToLower();
        var sizeLower = sizeNorm.ToLower();

        // Find matching active, not-deleted variant by key or case-insensitive text match
        var variant = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == productId && v.IsActive && !v.IsDeleted)
            .Where(v =>
                (v.ColorKey != null && v.ColorKey == colorNorm) ||
                (v.Color != null && v.Color.ToLower() == colorLower))
            .Where(v =>
                (v.SizeKey != null && v.SizeKey == sizeNorm) ||
                (v.Size != null && v.Size.ToLower() == sizeLower))
            .OrderBy(v => v.Id)
            .FirstOrDefaultAsync(ct);

        if (variant == null)
        {
            result.Errors.Add("Selected color/size variant is not available.");
            return result;
        }

        result.FoundVariantId = variant.Id;

        // Sum available qty across inventories (optionally scoped to store)
        var invQ = _db.ProductInventories.AsNoTracking()
            .Where(i => i.ProductVariantId == variant.Id && i.IsActive && !i.IsDeleted);
        if (preferredStoreId.HasValue)
        {
            var sid = preferredStoreId.Value;
            invQ = invQ.Where(i => i.StoreId == sid);
        }
        var available = await invQ.Select(i => (int?)i.QtyOnHand).SumAsync(ct) ?? 0;
        result.AvailableQty = available;
        result.IsAvailable = available >= requestedQty;
        if (!result.IsAvailable)
        {
            result.Errors.Add($"Insufficient stock: requested {requestedQty}, available {available}.");
        }
        return result;
    }
}
