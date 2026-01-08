using Microsoft.Extensions.Logging;
using OnlineContract.Data;
using System.Security.Claims;

namespace OnlineContract.Services;

public class CartMergeService
{
    private readonly CartService _cartService;
    private readonly AnonCartCacheService _anonSvc;
    private readonly ILogger<CartMergeService> _logger;

    public CartMergeService(CartService cartService, AnonCartCacheService anonSvc, ILogger<CartMergeService> logger)
    {
        _cartService = cartService;
        _anonSvc = anonSvc;
        _logger = logger;
    }

    public async Task MergeAnonIntoUserDraftAsync(int userId, Guid anonId, CancellationToken ct)
    {
        if (userId <= 0) throw new ArgumentException("Invalid userId");
        var items = await _anonSvc.GetAsync(anonId, ct);
        if (items == null || items.Count == 0)
        {
            _logger.LogInformation("No anon items to merge for {UserId}", userId);
            return;
        }

        _logger.LogInformation("Starting anon cart merge for {UserId} (AnonId={AnonId}) with {Count} items", userId, anonId, items.Count);

        foreach (var item in items)
        {
            await _cartService.AddToCartAsync(userId, item.VariantId, item.Qty, ct);
        }

        await _anonSvc.ClearAsync(anonId, ct);
        _logger.LogInformation("Anon cart merge completed for {UserId} (AnonId={AnonId})", userId, anonId);
    }
}
