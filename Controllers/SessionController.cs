using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineContract.Infrastructure;
using OnlineContract.Services;
using System.Text.Json;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/session")]
    public class SessionController : ControllerBase
    {
        private readonly SessionBridgeService _sessionSvc;
        private readonly IHostEnvironment _env;

        public SessionController(SessionBridgeService sessionSvc, IHostEnvironment env)
        {
            _sessionSvc = sessionSvc;
            _env = env;
        }

        [HttpPost("pending-add")]
        [AllowAnonymous]
        public async Task<IActionResult> SetPendingAdd()
        {
            try
            {
                var dto = await Request.ReadFromJsonAsync<Dictionary<string, int>>();
                var productId = (dto != null && dto.TryGetValue("productId", out var pid)) ? pid : 0;
                if (productId <= 0) return JsonResultHelper.StableJson(_env, new { message = "productId is required" }, StatusCodes.Status400BadRequest);
                _sessionSvc.SetPendingAdd(HttpContext, productId);
                try 
                { 
                    await Helpers.LoggerHelper.LogEventAsync(
                        HttpContext.RequestServices.GetRequiredService<Data.AppDbContext>(), 
                        Helpers.EventType.Information, 
                        "PendingAddToCart set", 
                        $"ProductId={productId}", 
                        Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext)); 
                } 
                catch (Exception ex) 
                { 
                    System.Console.Error.WriteLine($"Failed to log 'PendingAddToCart set' event for ProductId={productId}: {ex}");
                }
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpDelete("pending-add")]
        [AllowAnonymous]
        public async Task<IActionResult> ClearPendingAdd()
        {
            try
            {
                _sessionSvc.ClearPendingAdd(HttpContext);
                try { await Helpers.LoggerHelper.LogEventAsync(HttpContext.RequestServices.GetRequiredService<Data.AppDbContext>(), Helpers.EventType.Information, "PendingAddToCart cleared", string.Empty, Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext)); } catch { }
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch { return StatusCode(StatusCodes.Status500InternalServerError); }
        }

        [HttpGet("pending-add")]
        [AllowAnonymous]
        public IActionResult GetPendingAdd()
        {
            try
            {
                var pid = _sessionSvc.GetPendingAdd(HttpContext) ?? 0;
                return JsonResultHelper.StableJson(_env, new { productId = pid });
            }
            catch { return JsonResultHelper.StableJson(_env, new { productId = 0 }); }
        }
    }
}
