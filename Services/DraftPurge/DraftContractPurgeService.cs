using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using OnlineContract.Services.Notifications;
using System.Data;

namespace OnlineContract.Services.DraftPurge
{
    /// <summary>
    /// Service interface for the Draft Contract Purge process.
    /// </summary>
    public interface IDraftContractPurgeService
    {
        /// <summary>
        /// Executes the Draft Contract Purge process.
        /// </summary>
        Task<DraftContractPurgeResult> ExecuteAsync(DraftContractPurgeRequest request, CancellationToken ct = default);

        /// <summary>
        /// Checks if a task related to this process exists with Not Started or Started status.
        /// </summary>
        Task<bool> HasPendingTaskAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Service that orchestrates the Draft Contract Purge process.
    /// Deletes Draft contracts older than 30 days and all their dependent records.
    /// </summary>
    public class DraftContractPurgeService : IDraftContractPurgeService
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notificationService;

        public const string ProcessName = "Draft Contract Purge";
        private const int SystemUserId = 2;
        private const int BatchSize = 5000;
        private const int DaysOld = 30;

        public DraftContractPurgeService(
            AppDbContext db,
            INotificationService notificationService)
        {
            _db = db;
            _notificationService = notificationService;
        }

        public async Task<DraftContractPurgeResult> ExecuteAsync(DraftContractPurgeRequest request, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            var result = new DraftContractPurgeResult { Success = true };
            int? taskId = null;
            SchedulerProcess? process = null;

            var initiatedByUserId = request.Mode == DraftPurgeMode.Scheduled
                ? SystemUserId
                : request.TriggeredByUserId;

            try
            {
                // Get or update scheduler process with concurrency lock
                process = await GetSchedulerProcessWithLockAsync(ct);
                if (process == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Scheduler process not found.";
                    return result;
                }

                // Check if already running
                if (IsProcessRunning(process))
                {
                    result.Success = false;
                    result.ErrorMessage = "Process is already running.";
                    SendAlreadyRunningNotification(initiatedByUserId);
                    return result;
                }

                // Mark process as started
                await UpdateProcessOnStartAsync(process, ct);

                // Create task with Started status
                taskId = await CreateTaskAsync(initiatedByUserId, ct);

                // Log initiation
                var modeStr = request.Mode.ToString();
                await LogEventAsync(EventType.Information,
                    $"Draft Contract Purge has been initiated (mode: {modeStr}).",
                    null, initiatedByUserId, ct);

                // Send notification for manual runs
                if (request.Mode == DraftPurgeMode.Manual)
                {
                    SendStartNotification(initiatedByUserId);
                }

                // Execute the purge in batches
                await ExecutePurgeAsync(result, initiatedByUserId, ct);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await LogEventAsync(EventType.Error,
                    $"Draft Contract Purge failed: {ex.Message}",
                    ex.ToString(), initiatedByUserId, ct);
            }
            finally
            {
                result.Duration = DateTime.Now - startTime;

                // Update task status
                if (taskId.HasValue)
                {
                    var taskStatusId = await GetTaskStatusIdAsync(result.Status, ct);
                    await UpdateTaskAsync(taskId.Value, taskStatusId, ct);
                }

                // Update scheduler process
                if (process != null)
                {
                    await UpdateProcessOnFinishAsync(process, ct);
                }

                // Log summary
                await LogEventAsync(EventType.Information,
                    $"Successfully processed: {result.DeletedCount}",
                    null, initiatedByUserId, ct);
                await LogEventAsync(result.FailedCount > 0 ? EventType.Warning : EventType.Information,
                    $"Unsuccessfully processed: {result.FailedCount}",
                    null, initiatedByUserId, ct);

                // Log completion
                var statusStr = result.Status.ToString();
                var durationFmt = DateRangeHelper.FormatDuration(result.Duration);
                await LogEventAsync(
                    result.Status switch
                    {
                        DraftPurgeStatus.Successful => EventType.Information,
                        DraftPurgeStatus.SuccessfulNothingProcessed => EventType.Information,
                        DraftPurgeStatus.Warning => EventType.Warning,
                        _ => EventType.Error
                    },
                    $"Draft Contract Purge completed with status: {statusStr}. Duration: {durationFmt}.",
                    null, initiatedByUserId, ct);

                // Send completion notification
                SendCompletionNotification(initiatedByUserId, result);
            }

            return result;
        }

        public async Task<bool> HasPendingTaskAsync(CancellationToken ct = default)
        {
            // Resolve TaskStatus IDs for 'Not Started' and 'Started' from lookup_set
            var notStartedId = await GetLookupIdAsync("TaskStatus", "Not Started", ct);
            var startedId = await GetLookupIdAsync("TaskStatus", "Started", ct);

            var pendingStatusIds = new List<int>();
            if (notStartedId.HasValue) pendingStatusIds.Add(notStartedId.Value);
            if (startedId.HasValue) pendingStatusIds.Add(startedId.Value);

            // Fallback to hardcoded IDs if lookup fails
            if (pendingStatusIds.Count == 0)
            {
                pendingStatusIds.Add((int)Helpers.TaskStatus.NotStarted);
                pendingStatusIds.Add((int)Helpers.TaskStatus.Started);
            }

            return await _db.Tasks
                .AsNoTracking()
                .AnyAsync(t => t.Subject == ProcessName
                            && pendingStatusIds.Contains(t.Status)
                            && t.ContractId == null
                            && t.AssignedToUserId == SystemUserId, ct);
        }

        private async Task ExecutePurgeAsync(DraftContractPurgeResult result, int userId, CancellationToken ct)
        {
            // Resolve Draft state ID
            var draftStateId = await GetLookupIdAsync("ContractState", "Draft", ct) ?? (int)ContractState.Draft;

            while (!ct.IsCancellationRequested)
            {
                // Get batch of candidate contract IDs using sargable query
                var candidateIds = await GetPurgeCandidatesAsync(draftStateId, ct);

                if (candidateIds.Count == 0)
                    break;

                // Delete batch using stored procedure
                var (deleted, failed) = await DeleteBatchAsync(candidateIds, userId, ct);

                result.DeletedCount += deleted;
                result.FailedCount += failed;

                // Note: Individual contract deletions are logged in the stored procedure
            }
        }

        private async Task<List<int>> GetPurgeCandidatesAsync(int draftStateId, CancellationToken ct)
        {
            // Use raw SQL for sargable date comparison using DB function
            var sql = @"
                SELECT TOP (@BatchSize) c.contract_id
                FROM dbo.contract c
                WHERE c.is_active = 1
                  AND c.is_deleted = 0
                  AND c.contract_state = @DraftStateId
                  AND c.input_dt < DATEADD(DAY, -@DaysOld, dbo.GetLocalTime())
                ORDER BY c.contract_id ASC";

            var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(ct);

            var result = new List<int>();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new SqlParameter("@BatchSize", BatchSize));
            command.Parameters.Add(new SqlParameter("@DraftStateId", draftStateId));
            command.Parameters.Add(new SqlParameter("@DaysOld", DaysOld));

            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(reader.GetInt32(0));
            }

            return result;
        }

        private async Task<(int Deleted, int Failed)> DeleteBatchAsync(List<int> contractIds, int userId, CancellationToken ct)
        {
            if (contractIds.Count == 0)
                return (0, 0);

            var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(ct);

            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_DeleteDraftContractsBatch";
            command.CommandTimeout = 300; // 5 minutes for large batches

            // Create TVP parameter
            var tvp = new DataTable();
            tvp.Columns.Add("contract_id", typeof(int));
            foreach (var id in contractIds)
            {
                tvp.Rows.Add(id);
            }

            var tvpParam = new SqlParameter("@ContractIds", SqlDbType.Structured)
            {
                TypeName = "dbo.ContractIdList",
                Value = tvp
            };
            command.Parameters.Add(tvpParam);

            var deletedParam = new SqlParameter("@DeletedCount", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var failedParam = new SqlParameter("@FailedCount", SqlDbType.Int) { Direction = ParameterDirection.Output };
            command.Parameters.Add(deletedParam);
            command.Parameters.Add(failedParam);

            await command.ExecuteNonQueryAsync(ct);

            var deleted = (int)(deletedParam.Value ?? 0);
            var failed = (int)(failedParam.Value ?? 0);

            return (deleted, failed);
        }

        private async Task<SchedulerProcess?> GetSchedulerProcessWithLockAsync(CancellationToken ct)
        {
            try
            {
                if (!_db.Database.IsSqlite())
                {
                    var process = await _db.SchedulerProcesses
                        .FromSqlRaw(@"SELECT scheduler_process_id, name, description, last_end_dt, last_start_dt, 
                                             next_run_dt, duration_sec, duration_fmt, is_active, is_deleted, stamp 
                                      FROM dbo.scheduler_process WITH (UPDLOCK, HOLDLOCK) 
                                      WHERE name = 'Draft Contract Purge' AND is_deleted = 0")
                        .FirstOrDefaultAsync(ct);
                    return process;
                }
            }
            catch { /* Fall through to regular query */ }

            return await _db.SchedulerProcesses
                .Where(p => p.Name == ProcessName && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);
        }

        private static bool IsProcessRunning(SchedulerProcess process)
        {
            if (!process.LastStartDt.HasValue) return false;
            if (process.LastStartDt.Value.Year < 1901) return false;
            if (!process.LastEndDt.HasValue) return true;
            return process.LastEndDt.Value.Year < 1901 || process.LastEndDt < process.LastStartDt;
        }

        private async Task UpdateProcessOnStartAsync(SchedulerProcess process, CancellationToken ct)
        {
            process.LastStartDt = DateTime.Now;
            process.LastEndDt = null;
            _db.SchedulerProcesses.Update(process);
            await _db.SaveChangesAsync(ct);
        }

        private async Task UpdateProcessOnFinishAsync(SchedulerProcess process, CancellationToken ct)
        {
            process.LastEndDt = DateTime.Now;
            process.NextRunDt = GetNextRunAt0300(DateTime.Now);
            _db.SchedulerProcesses.Update(process);
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Computes the next local 03:00.
        /// If now is before today's 03:00, returns today at 03:00.
        /// Otherwise returns tomorrow at 03:00.
        /// </summary>
        public static DateTime GetNextRunAt0300(DateTime now)
        {
            var today0300 = new DateTime(now.Year, now.Month, now.Day, 3, 0, 0, DateTimeKind.Local);
            return now < today0300 ? today0300 : today0300.AddDays(1);
        }

        private async Task<int> CreateTaskAsync(int initiatedByUserId, CancellationToken ct)
        {
            // Resolve TaskPriority '4 - Low' and TaskStatus 'Started' from lookup_set
            var priorityId = await GetLookupIdAsync("TaskPriority", "4 - Low", ct) ?? (int)TaskPriority.Low;
            var statusStartedId = await GetLookupIdAsync("TaskStatus", "Started", ct) ?? (int)Helpers.TaskStatus.Started;

            var task = new TaskItem
            {
                Subject = ProcessName,
                Comments = "Draft Contract Purge has started.",
                InputDt = DateTime.Now,
                AssignedToUserId = SystemUserId,
                InitiatedByUserId = initiatedByUserId,
                Priority = priorityId,
                Status = statusStartedId,
                ReminderDt = new DateTime(1900, 1, 1),
                ContractId = null,
                CompletedDt = null,
                Stamp = 0
            };

            _db.Tasks.Add(task);
            await _db.SaveChangesAsync(ct);
            return task.Id;
        }

        private async Task UpdateTaskAsync(int taskId, int statusId, CancellationToken ct)
        {
            var task = await _db.Tasks.FindAsync(new object[] { taskId }, ct);
            if (task != null)
            {
                task.Status = statusId;
                task.CompletedDt = DateTime.Now;
                await _db.SaveChangesAsync(ct);
            }
        }

        private async Task<int> GetTaskStatusIdAsync(DraftPurgeStatus status, CancellationToken ct)
        {
            var (lookupValue, fallbackId) = status switch
            {
                DraftPurgeStatus.Successful => ("Successful", (int)Helpers.TaskStatus.Successful),
                DraftPurgeStatus.Warning => ("Warning", (int)Helpers.TaskStatus.Warning),
                DraftPurgeStatus.Failed => ("Failed", (int)Helpers.TaskStatus.Failed),
                DraftPurgeStatus.SuccessfulNothingProcessed => ("Successful Nothing Processed", (int)Helpers.TaskStatus.SuccessfulNothingProcessed),
                _ => ("Failed", (int)Helpers.TaskStatus.Failed)
            };

            return await GetLookupIdAsync("TaskStatus", lookupValue, ct) ?? fallbackId;
        }

        private async Task<int?> GetLookupIdAsync(string setName, string value, CancellationToken ct)
        {
            var lookup = await _db.LookupSets
                .AsNoTracking()
                .Where(l => l.SetName == setName && l.Value == value)
                .FirstOrDefaultAsync(ct);
            return lookup?.LookupSetId;
        }

        private async Task LogEventAsync(EventType type, string description, string? stackTrace, int userId, CancellationToken ct)
        {
            try
            {
                await LoggerHelper.LogEventAsync(_db, type, description, stackTrace, userId);
            }
            catch
            {
                // Logging should never break the main process
            }
        }

        private void SendStartNotification(int userId)
        {
            _notificationService.SendNotification(new NotificationMessage
            {
                Title = "Process Started",
                Body = $"The '{ProcessName}' process has been initiated. You will be notified upon completion.",
                Type = NotificationType.Information,
                UserId = userId
            });
        }

        private void SendAlreadyRunningNotification(int userId)
        {
            _notificationService.SendNotification(new NotificationMessage
            {
                Title = "Execution already in progress",
                Body = $"The '{ProcessName}' process cannot be started because another run is already in progress or pending. Please wait until the current run finishes.",
                Type = NotificationType.Warning,
                UserId = userId
            });
        }

        private void SendCompletionNotification(int userId, DraftContractPurgeResult result)
        {
            var (title, body, type) = result.Status switch
            {
                DraftPurgeStatus.Successful => (
                    "Process Completed",
                    $"The '{ProcessName}' process has successfully completed. Deleted: {result.DeletedCount} contracts.",
                    NotificationType.Information),
                DraftPurgeStatus.SuccessfulNothingProcessed => (
                    "Process Completed",
                    $"The '{ProcessName}' process completed — nothing to delete.",
                    NotificationType.Information),
                DraftPurgeStatus.Warning => (
                    "Process Completed with Warnings",
                    $"The '{ProcessName}' process completed with warnings. Deleted: {result.DeletedCount}, Failed: {result.FailedCount}. See Event Log for details.",
                    NotificationType.Warning),
                _ => (
                    "Process Failed",
                    $"The '{ProcessName}' process has failed. Please review the Event Log for details.",
                    NotificationType.Error)
            };

            _notificationService.SendNotification(new NotificationMessage
            {
                Title = title,
                Body = body,
                Type = type,
                UserId = userId
            });
        }
    }
}
