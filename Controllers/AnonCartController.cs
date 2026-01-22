using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineContract.Infrastructure;
using OnlineContract.Services;
using System.Text.Json;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/anon-cart")]
    public class AnonCartController : ControllerBase
    {
        private readonly AnonCartCacheService _svc;
        private readonly IHostEnvironment _env;
        private readonly Data.AppDbContext _db;

        public AnonCartController(AnonCartCacheService svc, IHostEnvironment env, Data.AppDbContext db)
        {
            _svc = svc;
            _env = env;
            _db = db;
        }

        [HttpPost("items")]
        [AllowAnonymous]
        public async Task<IActionResult> AddItem()
        {
            try
            {
                var dto = await Request.ReadFromJsonAsync<Dtos.AddToCartRequest>();
                if (dto == null || dto.ProductVariantId <= 0 || dto.Quantity < 1)
                    return JsonResultHelper.StableJson(_env, new { message = "Invalid request" }, StatusCodes.Status400BadRequest);
                var anonId = _svc.GetOrCreateAnonId();
                await _svc.UpsertAsync(anonId, dto.ProductVariantId, dto.Quantity, HttpContext.RequestAborted);
                try { await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Information, "Anon AddToCart", $"VariantId={dto.ProductVariantId}; Qty={dto.Quantity}; AnonId={anonId}", 0); } catch { }
                var payload = new { anonCartId = anonId };
                return JsonResultHelper.StableJson(_env, payload);
            }
            catch (InvalidOperationException)
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                try { await Helpers.LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Anon AddToCart failed", ex.ToString(), 0); } catch { }
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpGet("summary")]
        [AllowAnonymous]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            try
            {
                if (!_svc.TryGetAnonId(out var anonId))
                    return JsonResultHelper.StableJson(_env, new { itemCount = 0, items = Array.Empty<object>() });
                var items = await _svc.GetAsync(anonId, ct);
                var count = items.Sum(i => i.Qty);
                var payload = new { itemCount = count, items = items.Select(i => new { variantId = i.VariantId, qty = i.Qty }) };
                return JsonResultHelper.StableJson(_env, payload);
            }
            catch
            {
                return JsonResultHelper.StableJson(_env, new { itemCount = 0, items = Array.Empty<object>() });
            }
        }

        [HttpDelete]
        [AllowAnonymous]
        public async Task<IActionResult> Clear(CancellationToken ct)
        {
            try
            {
                if (_svc.TryGetAnonId(out var anonId))
                {
                    await _svc.ClearAsync(anonId, ct);
                }
                return NoContent();
            }
            catch { return NoContent(); }
        }
    }
}
