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
    [Route("api/approval-rules")]
    public class ApprovalRulesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ApprovalRulesController(AppDbContext db, IHostEnvironment env)
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
                var baseQuery = _db.ApprovalRules.AsNoTracking().Where(r => !r.IsDeleted);
                var totalCount = await baseQuery.CountAsync();
                var items = await baseQuery.OrderBy(r => r.Id)
                    .Skip(Math.Max(0, (pageIndex - 1) * size))
                    .Take(size)
                    .Select(r => new { id = r.Id, name = r.Name, description = r.Description, isActive = r.IsActive, contextId = r.ApprovalRuleContextId, stamp = r.Stamp })
                    .ToListAsync();
                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "ApprovalRules fetch failed", ex.ToString(), CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            try
            {
                if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
                var r = await _db.ApprovalRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (r == null) return JsonResultHelper.StableJson(_env, new { message = "Approval Rule not found." }, StatusCodes.Status404NotFound);
                return JsonResultHelper.StableJson(_env, new {
                    id = r.Id,
                    name = r.Name,
                    description = r.Description,
                    assignedToId = r.TaskAssignedToId,
                    amtThreshold = r.AmtThreshold,
                    pctThreshold = r.PctThreshold,
                    isActive = r.IsActive,
                    contextId = r.ApprovalRuleContextId,
                    stamp = r.Stamp
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "ApprovalRule get failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id)
        {
            try
            {
                if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
                var body = await Request.ReadFromJsonAsync<System.Collections.Generic.Dictionary<string, object?>>();
                if (body == null) return JsonResultHelper.StableJson(_env, new { success = false, message = "Invalid request" }, StatusCodes.Status400BadRequest);
                string getStr(string k) { return body.TryGetValue(k, out var v) ? v?.ToString() ?? string.Empty : string.Empty; }
                int getInt(string k) { return int.TryParse(getStr(k), out var n) ? n : 0; }
                decimal getDec(string k) { return decimal.TryParse(getStr(k), out var d) ? d : 0m; }
                var name = (getStr("name") ?? string.Empty).Trim();
                var desc = (getStr("description") ?? string.Empty).Trim();
                var assigned = getInt("assignedToId");
                var amt = getDec("amtThreshold");
                var pct = getDec("pctThreshold");
                var stamp = getInt("stamp");

                if (string.IsNullOrWhiteSpace(name)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Name is required." });
                if (string.IsNullOrWhiteSpace(desc)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Description is required." });
                if (assigned <= 0) return JsonResultHelper.StableJson(_env, new { success = false, message = "Assigned To is required." });
                var amtPos = amt > 0m; var pctPos = pct > 0m;
                if (!(amtPos ^ pctPos)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Enter either Amount Threshold OR Amount Percentage (strictly > 0)." });
                if (pct < 0m || pct > 100m) return JsonResultHelper.StableJson(_env, new { success = false, message = "Amount Percentage must be between 0 and 100." });

                var r = await _db.ApprovalRules.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (r == null) return JsonResultHelper.StableJson(_env, new { success = false, message = "Approval Rule not found." }, StatusCodes.Status404NotFound);
                if (r.Stamp != stamp) return JsonResultHelper.StableJson(_env, new { success = false, message = "The Approval Rule was changed by someone else. Please reload and try again." });

                r.Name = name;
                r.Description = desc;
                r.TaskAssignedToId = assigned;
                r.AmtThreshold = amt;
                r.PctThreshold = pct;
                r.Stamp = r.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Approval Rule updated", $"Id={r.Id}", CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "ApprovalRule update failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }

        [HttpPost("{id:int}/activate")]
        public async Task<IActionResult> Activate(int id, [FromQuery] int? stamp)
        {
            try
            {
                if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
                var r = await _db.ApprovalRules.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (r == null) return JsonResultHelper.StableJson(_env, new { success = false, message = "Approval Rule not found." }, StatusCodes.Status404NotFound);
                if (!stamp.HasValue || stamp.Value != r.Stamp) return JsonResultHelper.StableJson(_env, new { success = false, message = "The Approval Rule was changed by someone else. Please reload and try again." });
                r.IsActive = true; r.Stamp = r.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Approval Rule has been activated.", $"Id={r.Id}", CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Approval Rule activate failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }

        [HttpPost("{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id, [FromQuery] int? stamp)
        {
            try
            {
                if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
                var r = await _db.ApprovalRules.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (r == null) return JsonResultHelper.StableJson(_env, new { success = false, message = "Approval Rule not found." }, StatusCodes.Status404NotFound);
                if (!stamp.HasValue || stamp.Value != r.Stamp) return JsonResultHelper.StableJson(_env, new { success = false, message = "The Approval Rule was changed by someone else. Please reload and try again." });
                r.IsActive = false; r.Stamp = r.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Approval Rule has been deactivated.", $"Id={r.Id}", CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "ApprovalRule deactivate failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }

        [HttpPost("{id:int}/delete")]
        public async Task<IActionResult> Delete(int id, [FromQuery] int? stamp)
        {
            try
            {
                if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
                var r = await _db.ApprovalRules.FirstOrDefaultAsync(x => x.Id == id);
                if (r == null) return JsonResultHelper.StableJson(_env, new { success = false, message = "Approval Rule not found." }, StatusCodes.Status404NotFound);
                if (!stamp.HasValue || stamp.Value != r.Stamp) return JsonResultHelper.StableJson(_env, new { success = false, message = "The Approval Rule was changed by someone else. Please reload and try again." });
                r.IsActive = false; r.IsDeleted = true; r.Stamp = r.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Approval Rule has been deleted.", $"Id={r.Id}", CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { success = true });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Approval Rule delete failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }
    }
}