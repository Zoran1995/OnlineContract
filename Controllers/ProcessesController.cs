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
using OnlineContract.Services.Reports;
using OnlineContract.Services.DraftPurge;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/processes")]
    public class ProcessesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;
        private readonly IEomReportService _eomReportService;
        private readonly IDraftContractPurgeService _draftPurgeService;

        public ProcessesController(AppDbContext db, IHostEnvironment env, IEomReportService eomReportService, IDraftContractPurgeService draftPurgeService)
        {
            _db = db;
            _env = env;
            _eomReportService = eomReportService;
            _draftPurgeService = draftPurgeService;
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
                        isRunning = p.LastStartDt.HasValue && p.LastStartDt.Value.Year > 1900 &&
                                    (!p.LastEndDt.HasValue || p.LastEndDt.Value.Year < 1901 || p.LastEndDt < p.LastStartDt),
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

        /// <summary>
        /// Activates a scheduler process.
        /// </summary>
        [HttpPost("{id}/activate")]
        public async Task<IActionResult> Activate(int id, CancellationToken ct)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var process = await _db.SchedulerProcesses.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
                if (process == null)
                {
                    return NotFound(new { message = "Process not found." });
                }

                process.IsActive = true;
                await _db.SaveChangesAsync(ct);

                await LoggerHelper.LogEventAsync(_db, EventType.Information, 
                    $"Scheduler process '{process.Name}' (ID: {id}) has been activated.", null, CurrentUserId());

                return Ok(new { message = "Process activated successfully.", isActive = true });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Process activation failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to activate process." });
            }
        }

        /// <summary>
        /// Deactivates a scheduler process.
        /// </summary>
        [HttpPost("{id}/deactivate")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var process = await _db.SchedulerProcesses.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
                if (process == null)
                {
                    return NotFound(new { message = "Process not found." });
                }

                process.IsActive = false;
                await _db.SaveChangesAsync(ct);

                await LoggerHelper.LogEventAsync(_db, EventType.Information, 
                    $"Scheduler process '{process.Name}' (ID: {id}) has been deactivated.", null, CurrentUserId());

                return Ok(new { message = "Process deactivated successfully.", isActive = false });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Process deactivation failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to deactivate process." });
            }
        }

        /// <summary>
        /// Soft-deletes a scheduler process.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var process = await _db.SchedulerProcesses.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
                if (process == null)
                {
                    return NotFound(new { message = "Process not found." });
                }

                process.IsDeleted = true;
                await _db.SaveChangesAsync(ct);

                await LoggerHelper.LogEventAsync(_db, EventType.Information, 
                    $"Scheduler process '{process.Name}' (ID: {id}) has been deleted.", null, CurrentUserId());

                return Ok(new { message = "Process deleted successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Process deletion failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to delete process." });
            }
        }

        /// <summary>
        /// Manually triggers a process run.
        /// </summary>
        [HttpPost("{id}/run")]
        public async Task<IActionResult> Run(int id, CancellationToken ct)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var process = await _db.SchedulerProcesses.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
                
                if (process == null)
                {
                    return NotFound(new { message = "Process not found." });
                }

                var processName = process.Name ?? "Unknown Process";

                // Validation: Check if a task related to this process exists with Not Started or Started status
                // The relation is: task.subject = scheduler_process.name
                var hasPendingTask = await _eomReportService.HasPendingTaskAsync(processName, ct);
                if (hasPendingTask)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning,
                        $"[Scheduler] {processName} - Manual execution BLOCKED by user ID {CurrentUserId()}. " +
                        "Reason: A pending task already exists (status: Not Started or Started).",
                        null, CurrentUserId());

                    return Conflict(new
                    {
                        success = false,
                        message = $"The '{processName}' process cannot be started because another run is already in progress or pending. Please wait until the current run finishes."
                    });
                }

                // Check if process is running based on scheduler timestamps
                var isRunning = process.LastStartDt.HasValue && process.LastStartDt.Value.Year > 1900 &&
                               (!process.LastEndDt.HasValue || process.LastEndDt.Value.Year < 1901 || process.LastEndDt < process.LastStartDt);
                
                if (isRunning)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning,
                        $"[Scheduler] {processName} - Manual execution BLOCKED by user ID {CurrentUserId()}. " +
                        "Reason: Process is already running (last_start_dt > last_end_dt).",
                        null, CurrentUserId());

                    return Conflict(new
                    {
                        success = false,
                        message = $"The '{processName}' process cannot be started because another run is already in progress or pending. Please wait until the current run finishes."
                    });
                }

                // Handle specific process types
                if (process.Name == "EOM Report Generation")
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Information,
                        $"[Scheduler] {processName} - Manual execution triggered by user ID {CurrentUserId()}.",
                        null, CurrentUserId());

                    var request = new EomReportRequest
                    {
                        Mode = EomReportMode.Manual,
                        TriggeredByUserId = CurrentUserId()
                    };

                    var result = await _eomReportService.ExecuteAsync(request, ct);

                    // Build completion message based on status
                    var completionMessage = result.Status switch
                    {
                        EomReportStatus.Successful => $"The '{processName}' process completed successfully. Report saved to: {Path.GetFileName(result.FilePath ?? "N/A")}",
                        EomReportStatus.Warning => $"The '{processName}' process completed with warnings. Some contracts may have been skipped.",
                        EomReportStatus.SuccessfulNothingProcessed => $"The '{processName}' process completed. No contracts found in the specified period.",
                        _ => result.ErrorMessage ?? $"The '{processName}' process failed."
                    };

                    return Ok(new
                    {
                        success = result.Success,
                        processName = processName,
                        status = result.Status.ToString(),
                        filePath = result.FilePath,
                        message = completionMessage
                    });
                }

                if (process.Name == "Draft Contract Purge")
                {
                    // Use the service's HasPendingTaskAsync for the validation guard check
                    var hasPendingPurgeTask = await _draftPurgeService.HasPendingTaskAsync(ct);
                    if (hasPendingPurgeTask)
                    {
                        await LoggerHelper.LogEventAsync(_db, EventType.Warning,
                            $"[Scheduler] {processName} - Manual execution BLOCKED by user ID {CurrentUserId()}. " +
                            "Reason: Another instance is already running (pending task exists).",
                            null, CurrentUserId());

                        return Conflict(new
                        {
                            success = false,
                            message = $"The '{processName}' process cannot be started because another run is already in progress or pending. Please wait until the current run finishes."
                        });
                    }

                    await LoggerHelper.LogEventAsync(_db, EventType.Information,
                        $"[Scheduler] {processName} - Manual execution triggered by user ID {CurrentUserId()}.",
                        null, CurrentUserId());

                    var request = new DraftContractPurgeRequest
                    {
                        Mode = DraftPurgeMode.Manual,
                        TriggeredByUserId = CurrentUserId()
                    };

                    var result = await _draftPurgeService.ExecuteAsync(request, ct);

                    // Build completion message based on status
                    var completionMessage = result.Status switch
                    {
                        DraftPurgeStatus.Successful => $"The '{processName}' process completed successfully. Deleted: {result.DeletedCount} contracts.",
                        DraftPurgeStatus.Warning => $"The '{processName}' process completed with warnings. Deleted: {result.DeletedCount}, Failed: {result.FailedCount}.",
                        DraftPurgeStatus.SuccessfulNothingProcessed => $"The '{processName}' process completed. No draft contracts older than 30 days found.",
                        _ => result.ErrorMessage ?? $"The '{processName}' process failed."
                    };

                    return Ok(new
                    {
                        success = result.Success,
                        processName = processName,
                        status = result.Status.ToString(),
                        deletedCount = result.DeletedCount,
                        failedCount = result.FailedCount,
                        duration = result.Duration.TotalSeconds,
                        message = completionMessage
                    });
                }

                // For other process types, return not implemented
                return BadRequest(new { message = $"Manual run not implemented for process '{processName}'." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Process run failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to run process." });
            }
        }

        /// <summary>
        /// Cancels a running process.
        /// </summary>
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var process = await _db.SchedulerProcesses
                    .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
                
                if (process == null)
                {
                    return NotFound(new { message = "Process not found." });
                }

                var processName = process.Name ?? "Unknown Process";

                // Check if process is actually running
                var isRunning = process.LastStartDt.HasValue && process.LastStartDt.Value.Year > 1900 &&
                               (!process.LastEndDt.HasValue || process.LastEndDt.Value.Year < 1901 || process.LastEndDt < process.LastStartDt);
                
                if (!isRunning)
                {
                    // Silently succeed - process is not running, nothing to cancel
                    return Ok(new { success = true, message = $"Process '{processName}' is not running." });
                }

                // Find pending task for this process (Not Started or Started status)
                var notStartedId = await _db.LookupSets
                    .Where(l => l.SetName == "TaskStatus" && l.Value == "Not Started")
                    .Select(l => l.LookupSetId)
                    .FirstOrDefaultAsync(ct);
                var startedId = await _db.LookupSets
                    .Where(l => l.SetName == "TaskStatus" && l.Value == "Started")
                    .Select(l => l.LookupSetId)
                    .FirstOrDefaultAsync(ct);
                var cancelledId = await _db.LookupSets
                    .Where(l => l.SetName == "TaskStatus" && l.Value == "Cancelled")
                    .Select(l => l.LookupSetId)
                    .FirstOrDefaultAsync(ct);

                // Use fallback IDs if lookup fails
                if (notStartedId == 0) notStartedId = (int)Helpers.TaskStatus.NotStarted;
                if (startedId == 0) startedId = (int)Helpers.TaskStatus.Started;
                if (cancelledId == 0) cancelledId = (int)Helpers.TaskStatus.Cancelled;

                var pendingStatusIds = new[] { notStartedId, startedId };

                // Find the pending task
                var pendingTask = await _db.Tasks
                    .Where(t => t.Subject == processName 
                             && pendingStatusIds.Contains(t.Status)
                             && t.ContractId == null
                             && t.AssignedToUserId == 2) // System user
                    .FirstOrDefaultAsync(ct);

                if (pendingTask == null)
                {
                    // No pending task found, but process shows as running - just update process timestamps
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning,
                        $"[Scheduler] {processName} - Cancel requested but no pending task found. Updating process state only.",
                        null, CurrentUserId());
                }
                else
                {
                    // Update task status to Cancelled
                    pendingTask.Status = cancelledId;
                    pendingTask.CompletedDt = DateTime.Now;
                }

                // Calculate duration
                var now = DateTime.Now;
                var durationSec = process.LastStartDt.HasValue 
                    ? (int)(now - process.LastStartDt.Value).TotalSeconds 
                    : 0;
                var durationFmt = Helpers.DateRangeHelper.FormatDuration(TimeSpan.FromSeconds(durationSec));

                // Update process timestamps
                process.LastEndDt = now;
                process.DurationSec = durationSec;
                process.DurationFmt = durationFmt;

                await _db.SaveChangesAsync(ct);

                await LoggerHelper.LogEventAsync(_db, EventType.Information,
                    $"[Scheduler] {processName} - Process cancelled by user ID {CurrentUserId()}. Duration: {durationFmt}.",
                    null, CurrentUserId());

                return Ok(new 
                { 
                    success = true, 
                    message = $"Process '{processName}' has been cancelled.",
                    processName = processName,
                    duration = durationSec,
                    durationFmt = durationFmt
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Process cancel failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to cancel process." });
            }
        }
    }
}
