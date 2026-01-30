using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using OnlineContract.Services.Notifications;

namespace OnlineContract.Services.Reports
{
    /// <summary>
    /// Orchestrates the EOM (End of Month) Report generation process.
    /// </summary>
    public interface IEomReportService
    {
        /// <summary>
        /// Executes the EOM report generation process.
        /// </summary>
        Task<EomReportResult> ExecuteAsync(EomReportRequest request, CancellationToken ct = default);

        /// <summary>
        /// Gets a preview of the report data without generating a PDF.
        /// </summary>
        Task<EomReportResult> GetPreviewAsync(DateTime from, DateTime to, CancellationToken ct = default);

        /// <summary>
        /// Checks if a task related to a process (by name) exists with Not Started or Started status.
        /// </summary>
        Task<bool> HasPendingTaskAsync(string processName, CancellationToken ct = default);
    }

    public class EomReportService : IEomReportService
    {
        private readonly AppDbContext _db;
        private readonly IEomReportPdfRenderer _pdfRenderer;
        private readonly INotificationService _notificationService;
        private readonly EomReportSettings _settings;

        private const string ProcessName = "EOM Report Generation";
        private const int SystemUserId = 2;

        public EomReportService(
            AppDbContext db,
            IEomReportPdfRenderer pdfRenderer,
            INotificationService notificationService,
            IOptions<EomReportSettings> settings)
        {
            _db = db;
            _pdfRenderer = pdfRenderer;
            _notificationService = notificationService;
            _settings = settings.Value;
        }

        public async Task<EomReportResult> ExecuteAsync(EomReportRequest request, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            var result = new EomReportResult();
            int? taskId = null;
            SchedulerProcess? process = null;

            // Determine date range based on mode
            var (periodFrom, periodTo) = request.Mode == EomReportMode.Scheduled
                ? DateRangeHelper.GetPreviousMonthWindow(DateTime.Now)
                : DateRangeHelper.GetCurrentMonthToNowWindow(DateTime.Now);

            result.PeriodFrom = periodFrom;
            result.PeriodTo = periodTo;

            var initiatedByUserId = request.Mode == EomReportMode.Scheduled
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

                // Check if already running (last_start_dt set but last_end_dt is null or old 1900 date)
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
                taskId = await CreateTaskAsync(initiatedByUserId, periodFrom, periodTo, ct);

                // Log initiation
                var modeStr = request.Mode.ToString();
                await LogEventAsync(EventType.Information,
                    $"EOM Report Generation has been initiated for period {periodFrom:yyyy-MM-dd} – {periodTo:yyyy-MM-dd} (mode: {modeStr}).",
                    null, ct);

                // Send notification for manual runs
                if (request.Mode == EomReportMode.Manual)
                {
                    SendStartNotification(initiatedByUserId, periodFrom, periodTo);
                }

                // Aggregate contract data
                result.Summaries = await GetContractSummariesAsync(periodFrom, periodTo, result.Warnings, ct);

                // Generate PDF
                var filePath = await GeneratePdfAsync(result, ct);
                result.FilePath = filePath;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                await LogEventAsync(EventType.Error,
                    $"EOM Report Generation failed: {ex.Message}",
                    ex.ToString(), ct);
            }
            finally
            {
                result.Duration = DateTime.Now - startTime;

                // Update task status
                if (taskId.HasValue)
                {
                    var taskStatusId = GetTaskStatusId(result.Status);
                    await UpdateTaskAsync(taskId.Value, taskStatusId, ct);
                }

                // Update scheduler process
                if (process != null)
                {
                    await UpdateProcessOnFinishAsync(process, ct);
                }

                // Log completion
                var statusStr = result.Status.ToString();
                var durationFmt = DateRangeHelper.FormatDuration(result.Duration);
                var fileInfo = result.FilePath ?? "N/A";
                await LogEventAsync(
                    result.Success ? (result.Warnings.Count > 0 ? EventType.Warning : EventType.Information) : EventType.Error,
                    $"EOM Report Generation completed with status {statusStr}. File: {fileInfo}. Duration: {durationFmt}.",
                    null, ct);

                // Send completion notification
                SendCompletionNotification(initiatedByUserId, result);
            }

            return result;
        }

        public async Task<EomReportResult> GetPreviewAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            var result = new EomReportResult
            {
                PeriodFrom = from,
                PeriodTo = to,
                Success = true
            };

            result.Summaries = await GetContractSummariesAsync(from, to, result.Warnings, ct);
            return result;
        }

        /// <summary>
        /// Checks if a task related to a process (by name) exists with Not Started or Started status.
        /// The relation between task and process is: task.subject = scheduler_process.name.
        /// </summary>
        public async Task<bool> HasPendingTaskAsync(string processName, CancellationToken ct = default)
        {
            // Resolve TaskStatus IDs for 'Not Started' and 'Started' from lookup_set
            var notStartedId = await GetLookupIdAsync("TaskStatus", "Not Started", ct);
            var startedId = await GetLookupIdAsync("TaskStatus", "Started", ct);

            var pendingStatusIds = new List<int>();
            if (notStartedId.HasValue) pendingStatusIds.Add(notStartedId.Value);
            if (startedId.HasValue) pendingStatusIds.Add(startedId.Value);

            // Fallback to hardcoded IDs if lookup fails (but we prefer dynamic resolution)
            if (pendingStatusIds.Count == 0)
            {
                pendingStatusIds.Add((int)Helpers.TaskStatus.NotStarted);
                pendingStatusIds.Add((int)Helpers.TaskStatus.Started);
            }

            // Check if any SYSTEM task exists for this process:
            // - subject = processName
            // - status in pending (NotStarted/Started)
            // - ContractId is null (system task, not user task)
            // - AssignedToUserId = 2 (system user)
            const int SystemUserId = 2;
            return await _db.Tasks
                .AsNoTracking()
                .AnyAsync(t => t.Subject == processName 
                            && pendingStatusIds.Contains(t.Status)
                            && t.ContractId == null
                            && t.AssignedToUserId == SystemUserId, ct);
        }

        private async Task<SchedulerProcess?> GetSchedulerProcessWithLockAsync(CancellationToken ct)
        {
            // Try to get with row lock (SQL Server specific)
            // For SQLite test compatibility, fallback to regular query
            try
            {
                if (!_db.Database.IsSqlite())
                {
                    var process = await _db.SchedulerProcesses
                        .FromSqlRaw(@"SELECT scheduler_process_id, name, description, last_end_dt, last_start_dt, 
                                             next_run_dt, duration_sec, duration_fmt, is_active, is_deleted, stamp 
                                      FROM dbo.scheduler_process WITH (UPDLOCK, HOLDLOCK) 
                                      WHERE name = 'EOM Report Generation' AND is_deleted = 0")
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
            // Process is running if last_start_dt is set to a valid date 
            // and last_end_dt is either null or the default 1900-01-01
            if (!process.LastStartDt.HasValue) return false;
            if (process.LastStartDt.Value.Year < 1901) return false;
            if (!process.LastEndDt.HasValue) return true;
            return process.LastEndDt.Value.Year < 1901 || process.LastEndDt < process.LastStartDt;
        }

        private async Task UpdateProcessOnStartAsync(SchedulerProcess process, CancellationToken ct)
        {
            process.LastStartDt = DateTime.Now;
            process.LastEndDt = null; // Clear to indicate running
            _db.SchedulerProcesses.Update(process);
            await _db.SaveChangesAsync(ct);
        }

        private async Task UpdateProcessOnFinishAsync(SchedulerProcess process, CancellationToken ct)
        {
            process.LastEndDt = DateTime.Now;
            process.NextRunDt = DateRangeHelper.GetNextRunDt(DateTime.Now);
            // duration_sec and duration_fmt are computed columns, no need to set
            _db.SchedulerProcesses.Update(process);
            await _db.SaveChangesAsync(ct);
        }

        private async Task<int> CreateTaskAsync(int initiatedByUserId, DateTime periodFrom, DateTime periodTo, CancellationToken ct)
        {
            // Resolve TaskPriority.Normal (28) and TaskStatus.Started (31) from lookup_set
            var priorityId = await GetLookupIdAsync("TaskPriority", "3 - Normal", ct) ?? (int)TaskPriority.Normal;
            var statusStartedId = await GetLookupIdAsync("TaskStatus", "Started", ct) ?? (int)Helpers.TaskStatus.Started;

            var task = new TaskItem
            {
                Subject = ProcessName,
                Comments = $"Monthly EOM report generation has started. Period: {periodFrom:yyyy-MM-dd} to {periodTo:yyyy-MM-dd}.",
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

        private int GetTaskStatusId(EomReportStatus status)
        {
            return status switch
            {
                EomReportStatus.Successful => (int)Helpers.TaskStatus.Successful,
                EomReportStatus.Warning => (int)Helpers.TaskStatus.Warning,
                EomReportStatus.Failed => (int)Helpers.TaskStatus.Failed,
                EomReportStatus.SuccessfulNothingProcessed => (int)Helpers.TaskStatus.SuccessfulNothingProcessed,
                _ => (int)Helpers.TaskStatus.Failed
            };
        }

        private async Task<int?> GetLookupIdAsync(string setName, string value, CancellationToken ct)
        {
            var lookup = await _db.LookupSets
                .AsNoTracking()
                .Where(l => l.SetName == setName && l.Value == value)
                .FirstOrDefaultAsync(ct);
            return lookup?.LookupSetId;
        }

        private async Task<List<ContractStatusSummary>> GetContractSummariesAsync(
            DateTime from, DateTime to, List<string> warnings, CancellationToken ct)
        {
            var summaries = new List<ContractStatusSummary>();

            // Get ContractState lookup IDs
            var contractStates = await _db.LookupSets
                .AsNoTracking()
                .Where(l => l.SetName == "ContractState")
                .ToDictionaryAsync(l => l.Value, l => l.LookupSetId, ct);

            // Status configurations - report shows contracts entered in period grouped by current state
            var statusConfigs = new[]
            {
                ("Delivered", ContractState.Delivered),
                ("Rejected", ContractState.Rejected),
                ("Cancelled", ContractState.Cancelled),
                ("Written Off", ContractState.WrittenOff),
                ("Returned", ContractState.Returned),
                ("Refunded", ContractState.Refunded)
            };

            foreach (var (statusName, stateEnum) in statusConfigs)
            {
                try
                {
                    var (count, totalAmount) = await GetContractAggregateByInputDtAsync(stateEnum, from, to, ct);
                    
                    // Get lookup ID for this state
                    var statusId = contractStates.TryGetValue(statusName, out var id) ? id : (int)stateEnum;

                    summaries.Add(new ContractStatusSummary
                    {
                        StatusName = statusName,
                        StatusId = statusId,
                        Count = count,
                        TotalAmount = totalAmount
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to aggregate {statusName}: {ex.Message}");
                    summaries.Add(new ContractStatusSummary
                    {
                        StatusName = statusName,
                        StatusId = (int)stateEnum,
                        Count = 0,
                        TotalAmount = 0
                    });
                }
            }

            return summaries;
        }

        /// <summary>
        /// Aggregates contracts by state, filtering by input_dt (entry date) within the period.
        /// This shows contracts that were ENTERED in the period, grouped by their CURRENT state.
        /// </summary>
        private async Task<(int Count, decimal TotalAmount)> GetContractAggregateByInputDtAsync(
            ContractState state, DateTime from, DateTime toExclusive, CancellationToken ct)
        {
            // Query contracts entered in period [from, toExclusive) that are currently in the given state
            var query = _db.Contracts
                .AsNoTracking()
                .Where(c => !c.IsDeleted 
                         && c.ContractState == state
                         && c.EntryDate >= from 
                         && c.EntryDate < toExclusive);

            var count = await query.CountAsync(ct);
            var totalAmount = count > 0 ? await query.SumAsync(c => c.Amount, ct) : 0m;

            return (count, totalAmount);
        }

        private async Task<string?> GeneratePdfAsync(EomReportResult result, CancellationToken ct)
        {
            // Ensure output directory exists
            var outputPath = Path.IsPathRooted(_settings.OutputPath)
                ? _settings.OutputPath
                : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", _settings.OutputPath);

            // For production, use the configured path directly
            if (Directory.Exists(Path.GetDirectoryName(outputPath)) == false && Path.IsPathRooted(_settings.OutputPath))
            {
                outputPath = _settings.OutputPath;
            }

            // Default to ReportGeneration folder in solution root
            if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), _settings.OutputPath);
            }

            Directory.CreateDirectory(outputPath);

            var fileName = $"EOM_Report_{result.PeriodFrom:yyyyMM}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(outputPath, fileName);

            await _pdfRenderer.RenderAsync(result, filePath, _settings.CurrencyCulture, ct);

            return filePath;
        }

        private async Task LogEventAsync(EventType type, string description, string? stackTrace, CancellationToken ct)
        {
            await LoggerHelper.LogEventAsync(_db, type, description, stackTrace, SystemUserId);
        }

        private void SendStartNotification(int userId, DateTime periodFrom, DateTime periodTo)
        {
            _notificationService.SendNotification(new NotificationMessage
            {
                Title = "EOM Report Generation initiated",
                Body = $"The 'EOM Report Generation' process has been initiated for period {periodFrom:yyyy-MM-dd} to {periodTo:yyyy-MM-dd}. You will be notified upon completion.",
                Type = NotificationType.Information,
                UserId = userId
            });
        }

        private void SendCompletionNotification(int userId, EomReportResult result)
        {
            var message = result.Status switch
            {
                EomReportStatus.Successful => new NotificationMessage
                {
                    Title = "EOM report completed",
                    Body = "The EOM report has been successfully completed. The file is available in the ReportGeneration folder.",
                    Type = NotificationType.Information,
                    UserId = userId
                },
                EomReportStatus.Warning => new NotificationMessage
                {
                    Title = "EOM report completed with warnings",
                    Body = "Some contracts were skipped. Please review the Event Log for details.",
                    Type = NotificationType.Warning,
                    UserId = userId
                },
                EomReportStatus.SuccessfulNothingProcessed => new NotificationMessage
                {
                    Title = "EOM report completed",
                    Body = "The EOM report completed successfully. No contracts were found in the specified period.",
                    Type = NotificationType.Information,
                    UserId = userId
                },
                _ => new NotificationMessage
                {
                    Title = "EOM report failed",
                    Body = "An error occurred during processing. Please review the Event Log for details.",
                    Type = NotificationType.Error,
                    UserId = userId
                }
            };

            _notificationService.SendNotification(message);
        }

        private void SendAlreadyRunningNotification(int userId)
        {
            _notificationService.SendNotification(new NotificationMessage
            {
                Title = "EOM Report Generation",
                Body = "The process is already running. Please wait for it to complete.",
                Type = NotificationType.Warning,
                UserId = userId
            });
        }
    }
}
