using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using OnlineContract.Data;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public GroupsController(AppDbContext db) { _db = db; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] int page, [FromQuery] int pageSize)
        {
            try
            {
                var rc = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!int.TryParse(rc, out var roleId) || (roleId != 7 && roleId != 8))
                {
                    return StatusCode(StatusCodes.Status403Forbidden);
                }
            }
            catch
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var query = _db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted && x.IsActive);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim().ToLower();
                    query = query.Where(x =>
                        (x.Code ?? "").ToLower().Contains(s) ||
                        (x.FirstName ?? "").ToLower().Contains(s) ||
                        (x.LastName ?? "").ToLower().Contains(s));
                }

                var totalCount = await query.CountAsync();
                var items = await query.OrderBy(x => x.Id)
                    .Skip(Math.Max(0, (page - 1) * pageSize))
                    .Take(pageSize)
                    .Select(x => new { id = x.Id, code = x.Code, fullName = ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim() })
                    .ToListAsync();

                return new JsonResult(new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, OnlineContract.Helpers.EventType.Error, "Groups fetch failed", ex.ToString(), 2);
                return new JsonResult(new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }
    }
}