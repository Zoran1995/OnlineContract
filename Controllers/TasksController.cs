using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;
using OnlineContract.Models;
using OnlineContract.Dtos;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/tasks")]
    public class TasksController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public TasksController(AppDbContext db, IHostEnvironment env)
        {
            _db = db; _env = env;
        }

        [HttpGet("lookups")]
        [Authorize]
        public IActionResult GetLookups()
        {
            var priority = new[] {
                new { id = (int)OnlineContract.Helpers.TaskPriority.Urgent, name = nameof(OnlineContract.Helpers.TaskPriority.Urgent) },
                new { id = (int)OnlineContract.Helpers.TaskPriority.High, name = nameof(OnlineContract.Helpers.TaskPriority.High) },
                new { id = (int)OnlineContract.Helpers.TaskPriority.Normal, name = nameof(OnlineContract.Helpers.TaskPriority.Normal) },
                new { id = (int)OnlineContract.Helpers.TaskPriority.Low, name = nameof(OnlineContract.Helpers.TaskPriority.Low) },
            };
            var status = new[] {
                new { id = (int)OnlineContract.Helpers.TaskStatus.NotStarted, name = "Not Started" },
                new { id = (int)OnlineContract.Helpers.TaskStatus.Started, name = "Started" },
                new { id = (int)OnlineContract.Helpers.TaskStatus.Approved, name = "Approved" },
                new { id = (int)OnlineContract.Helpers.TaskStatus.Rejected, name = "Rejected" },
                new { id = (int)OnlineContract.Helpers.TaskStatus.Cancelled, name = "Cancelled" },
                new { id = (int)OnlineContract.Helpers.TaskStatus.Completed, name = "Completed" },
                new { id = (int)OnlineContract.Helpers.TaskStatus.Failed, name = "Failed" },
            };
            return JsonResultHelper.StableJson(_env, new { priority, status });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Get(int? taskId, int? contractId, int? priorityId, int? statusId, int page = 1, int pageSize = 10, string? sortBy = null, string? sortDir = null)
        {
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            var ownerId = await _db.AxUsers.AsNoTracking().Where(u => u.Id == uid).Select(u => (int?)u.OwnerId).FirstOrDefaultAsync() ?? 0;
            if (ownerId <= 0) ownerId = uid;
            var userIds = new List<int> { uid, ownerId };
            
            // Administrators can see ALL tasks
            bool isAdmin = UserContextHelper.IsAdministrator(HttpContext);
            
            // Administrators see ALL tasks
            // Manager/Worker see tasks they initiated OR tasks assigned to them (or their owner)
            IQueryable<TaskItem> q;
            if (isAdmin)
            {
                q = _db.Tasks.AsNoTracking();
            }
            else
            {
                q = _db.Tasks.AsNoTracking().Where(t => 
                    userIds.Contains(t.AssignedToUserId) || 
                    userIds.Contains(t.InitiatedByUserId));
            }
            if (taskId.HasValue && taskId.Value > 0) q = q.Where(t => t.Id == taskId.Value);
            if (contractId.HasValue && contractId.Value > 0) q = q.Where(t => t.ContractId == contractId.Value);
            if (priorityId.HasValue && priorityId.Value > 0) q = q.Where(t => t.Priority == priorityId.Value);
            if (statusId.HasValue && statusId.Value > 0) q = q.Where(t => t.Status == statusId.Value);

            var totalCount = await q.CountAsync();
            var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
            var sortMap = new System.Collections.Generic.Dictionary<string, System.Linq.Expressions.Expression<Func<TaskItem, object?>>> {
                { "id", e => e.Id },
                { "subject", e => e.Subject },
                { "comment", e => e.Comments },
                { "entryDate", e => e.InputDt },
                { "initiatedByUserId", e => e.InitiatedByUserId },
                { "contractId", e => e.ContractId ?? 0 },
                { "priority", e => e.Priority },
                { "status", e => e.Status }
            };
            var ordered = sortSpec == null
                ? q.OrderByDescending(e => e.InputDt).ThenBy(e => e.Id)
                : q.ApplySort(sortSpec, sortMap, e => e.Id);

            var items = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(_db.AxUsers.AsNoTracking(), t => t.InitiatedByUserId, u => u.Id, (t, u) => new { t, u })
                .Select(x => new {
                    id = x.t.Id,
                    subject = x.t.Subject,
                    comment = x.t.Comments,
                    entryDate = x.t.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                    initiatedByUserId = x.t.InitiatedByUserId,
                    initiatedByUserCode = x.u.Code ?? "",
                    assignedToUserId = x.t.AssignedToUserId,
                    contractId = x.t.ContractId,
                    priority = x.t.Priority,
                    priorityText = MapPriority(x.t.Priority),
                    status = x.t.Status,
                    statusText = MapStatus(x.t.Status)
                })
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            return JsonResultHelper.StableJson(_env, new { items, totalPages, totalCount, sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
        }

        [HttpGet("contract-pending")]
        [Authorize]
        public async Task<IActionResult> GetContractPending([FromQuery] int contractId)
        {
            if (contractId <= 0) return StatusCode(400);
            var pendingSet = new[] { (int)OnlineContract.Helpers.TaskStatus.NotStarted, (int)OnlineContract.Helpers.TaskStatus.Started };
            var tasks = await _db.Tasks.AsNoTracking()
                .Where(t => t.ContractId == contractId && pendingSet.Contains(t.Status))
                .Select(t => new { t.Id, t.Subject })
                .ToListAsync();
            var count = tasks.Count;
            // Return the first pending task's ID if available
            var taskId = tasks.FirstOrDefault()?.Id;
            return JsonResultHelper.StableJson(_env, new { hasPending = count > 0, count, taskId });
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetOne(int id)
        {
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            var ownerId = await _db.AxUsers.AsNoTracking().Where(u => u.Id == uid).Select(u => (int?)u.OwnerId).FirstOrDefaultAsync() ?? 0;
            if (ownerId <= 0) ownerId = uid;
            var userIds = new List<int> { uid, ownerId };
            
            // Administrators can see ALL tasks
            bool isAdmin = UserContextHelper.IsAdministrator(HttpContext);
            
            // Administrators can see all tasks
            // Manager/Worker see tasks they initiated OR tasks assigned to them (or their owner)
            TaskItem? t;
            if (isAdmin)
            {
                t = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            }
            else
            {
                t = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (
                    userIds.Contains(x.AssignedToUserId) || 
                    userIds.Contains(x.InitiatedByUserId)));
            }
            if (t == null) return StatusCode(404);
            string? initiatedByUserCode = null;
            try
            {
                initiatedByUserCode = await _db.AxUsers.AsNoTracking().Where(u => u.Id == t.InitiatedByUserId).Select(u => u.Code).FirstOrDefaultAsync();
            }
            catch {}
            var dto = new {
                id = t.Id,
                subject = t.Subject,
                comments = t.Comments,
                entryDate = t.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                assignedToUserId = t.AssignedToUserId,
                initiatedByUserId = t.InitiatedByUserId,
                initiatedByUserCode = initiatedByUserCode ?? string.Empty,
                priority = t.Priority,
                priorityText = MapPriority(t.Priority),
                status = t.Status,
                statusText = MapStatus(t.Status),
                reminderDt = t.ReminderDt?.ToString("yyyy-MM-dd HH:mm:ss"),
                contractId = t.ContractId,
                completedDt = t.CompletedDt?.ToString("yyyy-MM-dd HH:mm:ss"),
                stamp = t.Stamp
            };
            return JsonResultHelper.StableJson(_env, dto);
        }

        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateOne(int id, [FromBody] UpdateTaskDto dto)
        {
            if (dto == null) return StatusCode(400);
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            var ownerId = await _db.AxUsers.AsNoTracking().Where(u => u.Id == uid).Select(u => (int?)u.OwnerId).FirstOrDefaultAsync() ?? 0;
            if (ownerId <= 0) ownerId = uid;
            var assignIds = new[] { uid, ownerId };
            var t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id && assignIds.Contains(x.AssignedToUserId));
            if (t == null) return StatusCode(404);
            if (dto.PriorityId.HasValue && dto.PriorityId.Value > 0) t.Priority = dto.PriorityId.Value;
            if (dto.Comments != null) t.Comments = dto.Comments;
            t.Stamp = t.Stamp + 1;
            await _db.SaveChangesAsync();
            await LoggerHelper.LogEventAsync(_db, EventType.Information, "Task updated", $"TaskId={id}", uid);
            return JsonResultHelper.StableJson(_env, new { success = true });
        }

        [HttpGet("{id:int}/status/modal-data")]
        [Authorize]
        public async Task<IActionResult> GetStatusModalData(int id)
        {
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            var ownerId = await _db.AxUsers.AsNoTracking().Where(u => u.Id == uid).Select(u => (int?)u.OwnerId).FirstOrDefaultAsync() ?? 0;
            if (ownerId <= 0) ownerId = uid;
            var assignIds = new[] { uid, ownerId };
            bool isAdmin = UserContextHelper.IsAdministrator(HttpContext);
            
            TaskItem? t;
            if (isAdmin)
            {
                t = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            }
            else
            {
                t = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (
                    assignIds.Contains(x.AssignedToUserId) || assignIds.Contains(x.InitiatedByUserId)));
            }
            if (t == null) return StatusCode(404);
            var currentName = MapStatus(t.Status);
            var next = AllowedNextStatuses(t.Status).Select(s => new { id = s, name = MapStatus(s) }).ToArray();
            return JsonResultHelper.StableJson(_env, new { currentStatus = currentName, nextStatuses = next });
        }

        [HttpPost("{id:int}/status")]
        [Authorize]
        public async Task<IActionResult> SetStatus(int id, [FromBody] SetTaskStatusDto dto)
        {
            if (dto == null || dto.NextStatusId <= 0) return StatusCode(400);
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            var ownerId = await _db.AxUsers.AsNoTracking().Where(u => u.Id == uid).Select(u => (int?)u.OwnerId).FirstOrDefaultAsync() ?? 0;
            if (ownerId <= 0) ownerId = uid;
            var assignIds = new[] { uid, ownerId };
            bool isAdmin = UserContextHelper.IsAdministrator(HttpContext);
            
            TaskItem? t;
            if (isAdmin)
            {
                t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
            }
            else
            {
                t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id && (
                    assignIds.Contains(x.AssignedToUserId) || assignIds.Contains(x.InitiatedByUserId)));
            }
            if (t == null) return StatusCode(404);
            var allowed = AllowedNextStatuses(t.Status);
            if (!allowed.Contains(dto.NextStatusId)) return StatusCode(409); // conflict

            await using var tx = await _db.Database.BeginTransactionAsync();
            var currentName = MapStatus(t.Status);
            var nextName = MapStatus(dto.NextStatusId);
            t.Status = dto.NextStatusId;
            if (t.Status == (int)OnlineContract.Helpers.TaskStatus.Approved || t.Status == (int)OnlineContract.Helpers.TaskStatus.Completed || t.Status == (int)OnlineContract.Helpers.TaskStatus.Rejected || t.Status == (int)OnlineContract.Helpers.TaskStatus.Cancelled)
            {
                t.CompletedDt = DateTime.Now;
            }
            t.Stamp = t.Stamp + 1;
            await _db.SaveChangesAsync();

            // Add note for task status change
            var note = new OnlineContract.Models.Note
            {
                ProductId = null,
                ContractId = t.ContractId,
                Comment = (dto.Comment ?? string.Empty),
                Subject = $"Changed Task status from {currentName} to {nextName}",
                IsMain = false,
                IsDeleted = false,
                IsActive = true,
                InputDt = DateTime.Now,
                InputUserId = uid,
                LastModifiedById = uid,
                LastUpdatedDt = DateTime.Now,
                Stamp = 0
            };
            if (t.ContractId != null)
            {
                _db.Notes.Add(note);
            }
            await _db.SaveChangesAsync();

            // If this is an approval for a write-off task, transition contract now
            if (t.Status == (int)OnlineContract.Helpers.TaskStatus.Approved && t.ContractId != null && (t.Subject ?? string.Empty).IndexOf("Write-Off", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == t.ContractId.Value);
                if (contract != null)
                {
                    contract.ContractState = OnlineContract.Helpers.ContractState.WrittenOff;
                    contract.WrittenOffDt = DateTime.Now;
                    contract.LastModifiedById = uid;
                    contract.LastUpdatedDt = DateTime.Now;
                    contract.Stamp = contract.Stamp + 1;
                    await _db.SaveChangesAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Information, "Contract written off via task approval", $"ContractId={contract.Id}; TaskId={t.Id}", uid);
                }
            }

            await tx.CommitAsync();
            await LoggerHelper.LogEventAsync(_db, EventType.Information, "Task status changed", $"TaskId={id}; From={currentName}; To={nextName}", uid);
            return JsonResultHelper.StableJson(_env, new { success = true, newStatusName = nextName });
        }

        private static string MapPriority(int id)
        {
            return id switch
            {
                (int)OnlineContract.Helpers.TaskPriority.Urgent => nameof(OnlineContract.Helpers.TaskPriority.Urgent),
                (int)OnlineContract.Helpers.TaskPriority.High => nameof(OnlineContract.Helpers.TaskPriority.High),
                (int)OnlineContract.Helpers.TaskPriority.Normal => nameof(OnlineContract.Helpers.TaskPriority.Normal),
                (int)OnlineContract.Helpers.TaskPriority.Low => nameof(OnlineContract.Helpers.TaskPriority.Low),
                _ => "Unknown"
            };
        }

        private static string MapStatus(int id)
        {
            return id switch
            {
                (int)OnlineContract.Helpers.TaskStatus.NotStarted => "Not Started",
                (int)OnlineContract.Helpers.TaskStatus.Started => "Started",
                (int)OnlineContract.Helpers.TaskStatus.Approved => "Approved",
                (int)OnlineContract.Helpers.TaskStatus.Rejected => "Rejected",
                (int)OnlineContract.Helpers.TaskStatus.Cancelled => "Cancelled",
                (int)OnlineContract.Helpers.TaskStatus.Completed => "Completed",
                (int)OnlineContract.Helpers.TaskStatus.Failed => "Failed",
                (int)OnlineContract.Helpers.TaskStatus.Successful => "Successful",
                (int)OnlineContract.Helpers.TaskStatus.Warning => "Warning",
                39 => "Successful Nothing Processed", // SuccessfulNothingProcessed in lookup_set (id=40)
                40 => "Successful Nothing Processed", // SuccessfulNothingProcessed in lookup_set (id=40)
                _ => "Unknown"
            };
        }

        private static int[] AllowedNextStatuses(int current)
        {
            // Simple workflow: NotStarted -> Started/Cancelled; Started -> Approved/Rejected/Cancelled/Completed; Approved/Rejected -> Completed; Others -> []
            if (current == (int)OnlineContract.Helpers.TaskStatus.NotStarted)
                return new[] { (int)OnlineContract.Helpers.TaskStatus.Started, (int)OnlineContract.Helpers.TaskStatus.Cancelled };
            if (current == (int)OnlineContract.Helpers.TaskStatus.Started)
                return new[] { (int)OnlineContract.Helpers.TaskStatus.Approved, (int)OnlineContract.Helpers.TaskStatus.Rejected, (int)OnlineContract.Helpers.TaskStatus.Cancelled, (int)OnlineContract.Helpers.TaskStatus.Completed };
            if (current == (int)OnlineContract.Helpers.TaskStatus.Approved || current == (int)OnlineContract.Helpers.TaskStatus.Rejected)
                return new[] { (int)OnlineContract.Helpers.TaskStatus.Completed };
            return Array.Empty<int>();
        }
    }
}