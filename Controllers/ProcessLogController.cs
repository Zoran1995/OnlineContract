using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;
using OnlineContract.Models;

namespace OnlineContract.Controllers
{
    /// <summary>
    /// Controller for viewing system process log (tasks related to scheduled/manual process runs).
    /// Only accessible by Managers and Administrators.
    /// </summary>
    [ApiController]
    [Route("api/processlog")]
    public class ProcessLogController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        // Process-related task subjects that indicate scheduler/process runs
        private static readonly string[] ProcessSubjects = new[]
        {
            "EOM Report Generation",
            "Draft Contract Purge"
        };

        public ProcessLogController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Get(
            string? process = null,
            int? statusId = null,
            string? dateFrom = null,
            string? dateTo = null,
            int page = 1,
            int pageSize = 10,
            string? sortBy = null,
            string? sortDir = null)
        {
            // Only managers and administrators can access this endpoint
            var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!int.TryParse(roleClaim, out var roleId) || (roleId != 7 && roleId != 8))
            {
                return StatusCode(403, new { error = "Access denied" });
            }

            // Query tasks that are process-related (subject matches known process names)
            var q = _db.Tasks.AsNoTracking()
                .Where(t => ProcessSubjects.Contains(t.Subject));

            // Filter by process name (subject)
            if (!string.IsNullOrWhiteSpace(process))
            {
                var processLower = process.Trim().ToLower();
                q = q.Where(t => t.Subject.ToLower().Contains(processLower));
            }

            // Filter by status
            if (statusId.HasValue && statusId.Value > 0)
            {
                q = q.Where(t => t.Status == statusId.Value);
            }

            // Filter by date range
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var fromDate))
            {
                var fromStart = fromDate.Date;
                q = q.Where(t => t.InputDt >= fromStart);
            }
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var toDate))
            {
                var toEnd = toDate.Date.AddDays(1).AddSeconds(-1);
                q = q.Where(t => t.InputDt <= toEnd);
            }

            var totalCount = await q.CountAsync();

            // Sorting
            var sortSpec = string.IsNullOrWhiteSpace(sortBy) 
                ? null 
                : new SortSpec(sortBy.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
            
            var sortMap = new Dictionary<string, Expression<Func<TaskItem, object?>>>
            {
                { "id", t => t.Id },
                { "process", t => t.Subject },
                { "description", t => t.Comments },
                { "status", t => t.Status },
                { "initiatedBy", t => t.InitiatedByUserId },
                { "entryDate", t => t.InputDt }
            };

            var ordered = sortSpec == null
                ? q.OrderByDescending(t => t.InputDt).ThenByDescending(t => t.Id)
                : q.ApplySort(sortSpec, sortMap, t => t.Id);

            var tasksPage = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get user codes for initiated by
            var userIds = tasksPage.Select(t => t.InitiatedByUserId).Distinct().ToList();
            var userCodes = await _db.AxUsers.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Code })
                .ToDictionaryAsync(u => u.Id, u => u.Code ?? "");

            var items = tasksPage.Select(t => new
            {
                id = t.Id,
                process = t.Subject,
                description = t.Comments ?? "",
                status = t.Status,
                statusText = MapStatus(t.Status),
                initiatedBy = t.InitiatedByUserId == 2 ? "System" : (userCodes.TryGetValue(t.InitiatedByUserId, out var code) ? code : "Unknown"),
                entryDate = t.InputDt.ToString("yyyy-MM-dd HH:mm:ss")
            }).ToList();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            return JsonResultHelper.StableJson(_env, new { items, totalPages, totalCount });
        }

        private static string MapStatus(int status)
        {
            return status switch
            {
                30 => "Not Started",
                31 => "Started",
                32 => "Approved",
                33 => "Rejected",
                34 => "Cancelled",
                35 => "Completed",
                36 => "Failed",
                37 => "Successful",
                38 => "Warning",
                39 => "Successful Nothing Processed",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Cancels a running process task and updates the scheduler_process table.
        /// Similar to ProcessesController.Cancel but operates on a specific task ID.
        /// </summary>
        [HttpPost("{taskId}/cancel")]
        [Authorize]
        public async Task<IActionResult> Cancel(int taskId, CancellationToken ct)
        {
            // Only managers and administrators can access this endpoint
            var roleClaim = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!int.TryParse(roleClaim, out var roleId) || (roleId != 7 && roleId != 8))
            {
                return StatusCode(403, new { error = "Access denied" });
            }

            try
            {
                // Find the task
                var task = await _db.Tasks
                    .FirstOrDefaultAsync(t => t.Id == taskId, ct);

                if (task == null)
                {
                    return NotFound(new { message = "Task not found." });
                }

                // Check if task is a process-related task
                if (!ProcessSubjects.Contains(task.Subject))
                {
                    return BadRequest(new { message = "This task is not a process-related task." });
                }

                var processName = task.Subject;

                // Check if task is in Started status (31)
                var startedId = (int)Helpers.TaskStatus.Started;
                if (task.Status != startedId)
                {
                    return BadRequest(new { message = $"Task is not in Started status. Current status: {MapStatus(task.Status)}" });
                }

                // Find the corresponding scheduler_process
                var process = await _db.SchedulerProcesses
                    .FirstOrDefaultAsync(p => p.Name == processName && !p.IsDeleted, ct);

                // Get Cancelled status ID
                var cancelledId = await _db.LookupSets
                    .Where(l => l.SetName == "TaskStatus" && l.Value == "Cancelled")
                    .Select(l => l.LookupSetId)
                    .FirstOrDefaultAsync(ct);
                if (cancelledId == 0) cancelledId = (int)Helpers.TaskStatus.Cancelled;

                // Update task status to Cancelled
                task.Status = cancelledId;
                task.CompletedDt = DateTime.Now;

                // Update scheduler_process timestamps if found
                var durationFmt = "";
                if (process != null)
                {
                    var now = DateTime.Now;
                    var durationSec = process.LastStartDt.HasValue 
                        ? (int)(now - process.LastStartDt.Value).TotalSeconds 
                        : 0;
                    durationFmt = Helpers.DateRangeHelper.FormatDuration(TimeSpan.FromSeconds(durationSec));

                    process.LastEndDt = now;
                    process.DurationSec = durationSec;
                    process.DurationFmt = durationFmt;
                }

                await _db.SaveChangesAsync(ct);

                var userId = UserContextHelper.GetCurrentUserId(HttpContext);
                await LoggerHelper.LogEventAsync(_db, EventType.Information,
                    $"[ProcessLog] {processName} (Task #{taskId}) - Cancelled by user ID {userId}. {(string.IsNullOrEmpty(durationFmt) ? "" : $"Duration: {durationFmt}.")}",
                    null, userId);

                return Ok(new 
                { 
                    success = true, 
                    message = $"Process '{processName}' (Task #{taskId}) has been cancelled.",
                    processName = processName,
                    taskId = taskId
                });
            }
            catch (Exception ex)
            {
                var userId = UserContextHelper.GetCurrentUserId(HttpContext);
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "ProcessLog cancel failed", ex.ToString(), userId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to cancel process." });
            }
        }
    }
}
