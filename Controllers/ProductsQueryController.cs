using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/products")] 
    public class ProductsQueryController : ControllerBase
    {
        private readonly IHostEnvironment _env;
        private readonly AppDbContext _db;

        public ProductsQueryController(IHostEnvironment env, AppDbContext db)
        {
            _env = env; _db = db;
        }

        [HttpGet("cards")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCards([FromServices] Services.ProductQueryService svc)
        {
            var cards = await svc.GetProductCardsAsync(HttpContext.RequestAborted);
            try
            {
                var uid = UserContextHelper.GetCurrentUserId(HttpContext);
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product cards load", $"Count={cards.Count}", uid);
            }
            catch { }
            return JsonResultHelper.StableJson(_env, cards);
        }
    }
}
