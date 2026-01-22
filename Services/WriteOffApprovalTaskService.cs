using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;

namespace OnlineContract.Services
{
    public class WriteOffApprovalTaskService
    {
        private readonly AppDbContext _db;
        public WriteOffApprovalTaskService(AppDbContext db)
        {
            _db = db;
        }

        public async Task CreateWriteOffApprovalTaskIfNeededAsync(int contractId, int currentUserId, CancellationToken ct)
        {
            // Use GlobalEnums for ApprovalRuleContext
            int contextId = (int)ApprovalRuleContext.ContractWriteOff;

            // Read contract amount (use current persisted value after state update)
            var amount = await _db.Contracts.AsNoTracking()
                .Where(c => c.Id == contractId)
                .Select(c => (decimal?)c.Amount)
                .FirstOrDefaultAsync(ct) ?? 0m;

            // Select matching rule (provider-agnostic: load and filter in memory)
            var rules = await _db.ApprovalRules.AsNoTracking()
                .Where(r => r.ApprovalRuleContextId == contextId)
                .OrderByDescending(r => r.Id)
                .ToListAsync(ct);
            var latest = rules.FirstOrDefault();
            ApprovalRule? rule = null;
            if (latest != null && latest.IsActive && !latest.IsDeleted && amount >= latest.AmtThreshold)
            {
                rule = latest;
            }
            if (rule == null) return;

            // Use GlobalEnums for TaskPriority and TaskStatus
            int priorityHighId = (int)TaskPriority.High;
            int statusNotStartedId = (int)OnlineContract.Helpers.TaskStatus.NotStarted;
            int statusStartedId = (int)OnlineContract.Helpers.TaskStatus.Started;

            var subject = $"Contract {contractId} Write-Off";
            // Idempotency: avoid duplicate active tasks for the same subject & contract
            var exists = await _db.Tasks.AsNoTracking()
                .Where(t => t.ContractId == contractId && t.Subject == subject && (t.Status == statusNotStartedId || (statusStartedId > 0 && t.Status == statusStartedId)))
                .AnyAsync(ct);
            if (exists) return;

            var now = DateTime.Now;
            var task = new TaskItem
            {
                Subject = subject,
                Comments = "Auto-created task for contract write-off approval. Please review and proceed.",
                InputDt = now,
                AssignedToUserId = rule.TaskAssignedToId,
                InitiatedByUserId = currentUserId,
                Priority = priorityHighId,
                Status = statusNotStartedId,
                ReminderDt = now.AddDays(1),
                ContractId = contractId,
                CompletedDt = new DateTime(1900, 1, 1),
                Stamp = 0
            };
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync(ct);

            // Log success
            await LoggerHelper.LogEventAsync(_db, EventType.Information, "Write-off approval task created", $"ContractId={contractId}; TaskId={task.Id}; RuleId={rule.Id}", currentUserId);
        }
    }
}
