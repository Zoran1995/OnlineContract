using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineContract.Infrastructure;
using OnlineContract.Services;
using System.Security.Claims;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/cart")]
    public class CartController : ControllerBase
    {
        private readonly CartService _cartSvc;
        private readonly CartMergeService _mergeSvc;
        private readonly AnonCartCacheService _anonSvc;
        private readonly IHostEnvironment _env;
        private readonly Data.AppDbContext _db;

        public CartController(CartService cartSvc, CartMergeService mergeSvc, AnonCartCacheService anonSvc, IHostEnvironment env, Data.AppDbContext db)
        {
            _cartSvc = cartSvc;
            _mergeSvc = mergeSvc;
            _anonSvc = anonSvc;
            _env = env;
            _db = db;
        }

        [HttpPost("items")]
        [Authorize]
        public async Task<IActionResult> AddToCart()
        {
            try
            {
                var dto = await Request.ReadFromJsonAsync<Dtos.AddToCartRequest>();
                if (dto == null || dto.ProductVariantId <= 0 || dto.Quantity < 1)
                    return JsonResultHelper.StableJson(_env, new { message = "Invalid payload" }, StatusCodes.Status400BadRequest);

                var uid = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                var res = await _cartSvc.AddToCartAsync(uid, dto.ProductVariantId, dto.Quantity, HttpContext.RequestAborted);
                try { await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Information, "AddToCart", $"VariantId={dto.ProductVariantId}; Qty={dto.Quantity}; ContractId={res.ContractId}", uid); } catch { }
                return JsonResultHelper.StableJson(_env, res);
            }
            catch (InvalidOperationException ex)
            {
                return JsonResultHelper.StableJson(_env, new { message = ex.Message }, StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                try { await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "AddToCart failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext)); } catch { }
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("merge-anon")]
        [Authorize]
        public async Task<IActionResult> MergeAnon()
        {
            try
            {
                var userIdClaim = User.FindFirst("sub") ?? User.FindFirst("userId") ?? User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null) return Unauthorized();
                if (!_anonSvc.TryGetAnonId(out var anonId)) return JsonResultHelper.StableJson(_env, new { success = true });
                await _mergeSvc.MergeAnonIntoUserDraftAsync(int.Parse(userIdClaim.Value), anonId, HttpContext.RequestAborted);
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                try { await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Merge anon failed", ex.ToString(), 0); } catch { }
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
