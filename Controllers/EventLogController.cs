using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/event-log")]
    public class EventLogController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public EventLogController(AppDbContext db, IHostEnvironment env)
        {
            _db = db; _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Get(int userId, int type, DateTime? from, DateTime? to, int page, int pageSize, string? sortBy, string? sortDir)
        {
            try
            {
                var query = _db.EventLogs.AsNoTracking().Where(e => e.EventLogId > 0);
                if (userId > 0) query = query.Where(e => e.UserId == userId);
                if (type == 0)
                {
                    query = query.Where(x => x.EventTypeId == 2 || x.EventTypeId == 3 || x.EventTypeId == 4);
                }
                else if (type > 0)
                {
                    var mappedType = type == 1 ? 2 : type == 2 ? 3 : type == 3 ? 4 : type;
                    query = query.Where(x => x.EventTypeId == mappedType);
                }
                if (from.HasValue) query = query.Where(x => x.InputDt >= from.Value);
                if (to.HasValue) query = query.Where(x => x.InputDt <= to.Value);

                var totalCount = await query.CountAsync();

                var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
                var sortMap = new System.Collections.Generic.Dictionary<string, System.Linq.Expressions.Expression<Func<Models.EventLog, object?>>>
                {
                    { "id", e => e.EventLogId },
                    { "type", e => e.EventTypeId },
                    { "inputDt", e => e.InputDt },
                    { "description", e => e.Description },
                    { "user", e => e.UserId }
                };

                var ordered = sortSpec == null
                    ? query.OrderByDescending(e => e.InputDt).ThenBy(e => e.EventLogId)
                    : query.ApplySort(sortSpec, sortMap, e => e.EventLogId);

                var items = await (from e in ordered
                                   join u in _db.AxUsers on e.UserId equals u.Id into users
                                   from u in users.DefaultIfEmpty()
                                   select new
                                   {
                                       e.EventLogId,
                                       e.EventTypeId,
                                       e.InputDt,
                                       e.Description,
                                       u.Code,
                                       e.StackTrace
                                   })
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .Select(e => new
                                   {
                                       id = e.EventLogId,
                                       type = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                                       date = e.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                                       description = e.Description,
                                       user = e.Code,
                                       stackTrace = e.StackTrace
                                   })
                                   .ToListAsync();

                return JsonResultHelper.StableJson(_env, new { items, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), totalCount, sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "EventLog fetch failed", ex.ToString(), userId);
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalPages = 0, totalCount = 0 });
            }
        }

        [HttpGet("export")]
        [Authorize]
        public async Task<IActionResult> Export(int userId, int type, DateTime? from, DateTime? to)
        {
            try
            {
                var q =
                    from e in _db.EventLogs
                    join u in _db.AxUsers on e.UserId equals u.Id into users
                    from u in users.DefaultIfEmpty()
                    select new
                    {
                        e.EventLogId,
                        EventTypeId = e.EventTypeId,
                        TypeName = e.EventTypeId == 2 ? "Information" : e.EventTypeId == 3 ? "Warning" : "Error",
                        e.InputDt,
                        e.Description,
                        UserFullName = u != null ? (u.FirstName + " " + u.LastName).Trim() : $"User {e.UserId}",
                        e.StackTrace
                    };

                if (type == 0)
                {
                    q = q.Where(x => x.EventTypeId == 2 || x.EventTypeId == 3 || x.EventTypeId == 4);
                }
                else if (type > 0)
                {
                    var mappedType = type == 1 ? 2 : type == 2 ? 3 : type == 3 ? 4 : type;
                    q = q.Where(x => x.EventTypeId == mappedType);
                }

                if (from.HasValue) q = q.Where(x => x.InputDt >= from.Value);
                if (to.HasValue) q = q.Where(x => x.InputDt <= to.Value);

                var logs = await q.OrderByDescending(x => x.InputDt).ToListAsync();

                string EscapeCsv(object? value)
                {
                    if (value == null) return string.Empty;
                    var s = value.ToString() ?? string.Empty;
                    s = s.Replace("\r\n", "\n").Replace('\r', '\n');
                    s = s.Replace("\"", "\"\"");
                    if (s.IndexOfAny(new char[] { ',', '"', '\n' }) >= 0)
                    {
                        s = '"' + s + '"';
                    }
                    return s;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Id,Type,Date,Description,User,StackTrace");
                foreach (var e in logs)
                {
                    sb.Append(EscapeCsv(e.EventLogId)); sb.Append(',');
                    sb.Append(EscapeCsv(e.TypeName)); sb.Append(',');
                    sb.Append(EscapeCsv(e.InputDt.ToString("yyyy-MM-dd HH:mm:ss"))); sb.Append(',');
                    sb.Append(EscapeCsv(e.Description)); sb.Append(',');
                    sb.Append(EscapeCsv(e.UserFullName)); sb.Append(',');
                    sb.Append(EscapeCsv(e.StackTrace));
                    sb.AppendLine();
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var fname = $"EventLog_{ts}.csv";
                return File(bytes, "text/csv; charset=utf-8", fname);
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Export failed", ex.ToString(), userId);
                return StatusCode(500);
            }
        }
    }
}
