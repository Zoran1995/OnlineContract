using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Dtos;
using OnlineContract.Models;

namespace OnlineContract.Services;

/// <summary>
/// Handles cart operations via draft contracts and contract_det rows.
/// </summary>
public class CartService
{
    private readonly AppDbContext _db;

    public CartService(AppDbContext db) { _db = db; }

    /// <summary>
    /// Add item to user's draft contract. Validates availability and uses server price.
    /// </summary>
    public async Task<AddToCartResponse> AddToCartAsync(int userId, int productVariantId, int quantity, CancellationToken ct = default)
    {
        if (userId <= 0) throw new ArgumentException("Invalid userId");
        if (productVariantId <= 0) throw new ArgumentException("Invalid productVariantId");
        if (quantity < 1) throw new ArgumentException("Quantity must be >= 1");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Validate variant
        var variant = await _db.ProductVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == productVariantId && v.IsActive && !v.IsDeleted, ct);
        if (variant == null) throw new InvalidOperationException("Variant not found or inactive");

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == variant.ProductId && p.IsActive && !p.IsDeleted, ct);
        if (product == null) throw new InvalidOperationException("Product not found or inactive");

        // Available quantity across stores
        var availableQty = await _db.ProductInventories.AsNoTracking()
            .Where(i => i.ProductVariantId == productVariantId && !i.IsDeleted && i.IsActive)
            .Select(i => (int?)i.QtyOnHand).SumAsync(ct) ?? 0;

        if (availableQty > 0 && quantity > availableQty)
        {
            // Reject over-quantity
            throw new InvalidOperationException("Nemamo toliko na stanju, molimo smanjite količinu.");
        }

        string? warning = null;
        if (availableQty == 0)
        {
            warning = "Currently unavailable. We will contact you ASAP.";
        }

        // Find or create user's draft contract
        var draft = await _db.Contracts.FirstOrDefaultAsync(c => (c.InputUserId ?? 0) == userId && c.ContractState == Helpers.ContractState.Draft && c.IsActive && !c.IsDeleted, ct);
        var increment = variant.Amount * quantity;
        if (draft == null)
        {
            draft = new Contract
            {
                InputUserId = userId,
                LastModifiedById = userId,
                LastUpdatedDt = DateTime.Now,
                EntryDate = DateTime.Now,
                ContractState = Helpers.ContractState.Draft,
                IsActive = true,
                IsDeleted = false,
                Amount = increment,
                Stamp = 0
            };
            _db.Contracts.Add(draft);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // Update draft amount (server authoritative)
            draft.Amount = draft.Amount + increment;
            draft.LastModifiedById = userId;
            draft.LastUpdatedDt = DateTime.Now;
            draft.Stamp = draft.Stamp + 1;
            await _db.SaveChangesAsync(ct);
        }

        // Insert contract_det line
        var det = new ContractDet
        {
            ContractId = draft.Id,
            ProductVariantId = productVariantId,
            Quantity = quantity,
            Amount = variant.Amount,
            ProductName = product.Name ?? string.Empty,
            Size = variant.Size,
            Color = variant.Color,
            ItemStateId = Helpers.ProductStateInOrder.Draft,
            InputUserId = userId,
            InputDt = DateTime.Now,
            LastModifiedById = userId,
            LastUpdatedDt = DateTime.Now,
            IsActive = true,
            IsDeleted = false,
            Stamp = 0
        };
        _db.ContractDets.Add(det);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return new AddToCartResponse { ContractId = draft.Id, Warning = warning };
    }
}
