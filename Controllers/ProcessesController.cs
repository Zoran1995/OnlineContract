using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/processes")]
    public class ProcessesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ProcessesController(AppDbContext db, IHostEnvironment env)
        {
            _db = db; _env = env;
        }

        private bool CanManageProducts() => Infrastructure.UserContextHelper.CanManageProducts(HttpContext);
        private int CurrentUserId() => Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int page, [FromQuery] int pageSize)
        {
            try
            {
                if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);
                var baseQuery = _db.SchedulerProcesses.AsNoTracking().Where(p => !p.IsDeleted);
                var totalCount = await baseQuery.CountAsync();
                var items = await baseQuery.OrderBy(p => p.Id)
                    .Skip(Math.Max(0, (pageIndex - 1) * size))
                    .Take(size)
                    .Select(p => new {
                        id = p.Id,
                        name = p.Name,
                        description = p.Description,
                        lastStartDt = p.LastStartDt,
                        lastEndDt = p.LastEndDt,
                        nextRunDt = p.NextRunDt,
                        durationSec = p.DurationSec,
                        durationFmt = p.DurationFmt,
                        isActive = p.IsActive,
                        stamp = p.Stamp
                    })
                    .ToListAsync();
                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Processes fetch failed", ex.ToString(), CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }
    }
}
