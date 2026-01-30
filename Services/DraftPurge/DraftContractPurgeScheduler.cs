using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlineContract.Data;
using OnlineContract.Helpers;

namespace OnlineContract.Services.DraftPurge
{
    /// <summary>
    /// Background service that schedules Draft Contract Purge every day at 03:00 (local time).
    /// </summary>
    public class DraftContractPurgeScheduler : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DraftContractPurgeScheduler> _logger;

        private const string ProcessName = "Draft Contract Purge";
        private const int SystemUserId = 2;

        public DraftContractPurgeScheduler(IServiceProvider serviceProvider, ILogger<DraftContractPurgeScheduler> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Draft Contract Purge Scheduler started.");
            await LogEventToDbAsync(EventType.Information, 
                $"[Scheduler] {ProcessName} scheduler started. Scheduled to run daily at 03:00.", 
                null, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var nextRun = await GetNextScheduledRunAsync(stoppingToken);

                    if (nextRun.HasValue && nextRun.Value <= now)
                    {
                        // Check if process exists in database
                        var processExists = await ProcessExistsAsync(stoppingToken);
                        if (!processExists)
                        {
                            _logger.LogError("Draft Contract Purge process not found in database, cannot execute.");
                            await LogEventToDbAsync(EventType.Error, 
                                $"[Scheduler] {ProcessName} - FAILED TO START: Process not found in scheduler_process table. " +
                                "Reason: The process record may have been deleted or does not exist in the database.", 
                                null, stoppingToken);
                            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                            continue;
                        }

                        // Check if process is active
                        if (await IsProcessActiveAsync(stoppingToken))
                        {
                            // Check if already running (pending task check)
                            if (!await HasPendingTaskAsync(stoppingToken))
                            {
                                _logger.LogInformation("Executing scheduled Draft Contract Purge at {Date}", now);
                                await LogEventToDbAsync(EventType.Information, 
                                    $"[Scheduler] {ProcessName} - Scheduled execution triggered at {now:yyyy-MM-dd HH:mm:ss}. Mode: Scheduled.", 
                                    null, stoppingToken);
                                await ExecutePurgeAsync(stoppingToken);
                            }
                            else
                            {
                                _logger.LogInformation("Draft Contract Purge task is already running, skipping scheduled run.");
                                await LogEventToDbAsync(EventType.Warning, 
                                    $"[Scheduler] {ProcessName} - SKIPPED: Another instance is already running. " +
                                    $"Scheduled run at {now:yyyy-MM-dd HH:mm:ss} was not executed. " +
                                    "Reason: A pending task with status 'Not Started' or 'Started' already exists for this process.", 
                                    null, stoppingToken);
                                // Update next_run_dt to avoid repeated checks
                                await UpdateNextRunOnlyAsync(stoppingToken);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("Draft Contract Purge process is not active, skipping scheduled run.");
                            await LogEventToDbAsync(EventType.Warning, 
                                $"[Scheduler] {ProcessName} - SKIPPED: Process is deactivated (is_active = 0). " +
                                $"Scheduled run at {now:yyyy-MM-dd HH:mm:ss} was not executed. " +
                                "Reason: An administrator has deactivated this process.", 
                                null, stoppingToken);
                            await UpdateNextRunOnlyAsync(stoppingToken);
                        }
                    }

                    // Wait until next check
                    var delay = CalculateDelay(nextRun, now);
                    _logger.LogDebug("Next scheduler check in {Delay}", delay);
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Draft Contract Purge Scheduler loop");
                    await LogEventToDbAsync(EventType.Error, 
                        $"[Scheduler] {ProcessName} - ERROR IN SCHEDULER LOOP: {ex.Message}. " +
                        "Reason: An unexpected error occurred in the background scheduler. The scheduler will retry in 1 minute.", 
                        ex.ToString(), stoppingToken);
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Draft Contract Purge Scheduler stopped.");
            await LogEventToDbAsync(EventType.Information, 
                $"[Scheduler] {ProcessName} scheduler stopped.", 
                null, CancellationToken.None);
        }

        private async Task<DateTime?> GetNextScheduledRunAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var process = await db.SchedulerProcesses
                .AsNoTracking()
                .Where(p => p.Name == ProcessName && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            return process?.NextRunDt;
        }

        private async Task<bool> IsProcessActiveAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var process = await db.SchedulerProcesses
                .AsNoTracking()
                .Where(p => p.Name == ProcessName && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            return process?.IsActive ?? false;
        }

        private async Task<bool> HasPendingTaskAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var purgeService = scope.ServiceProvider.GetRequiredService<IDraftContractPurgeService>();
            return await purgeService.HasPendingTaskAsync(ct);
        }

        private async Task ExecutePurgeAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var purgeService = scope.ServiceProvider.GetRequiredService<IDraftContractPurgeService>();

            var request = new DraftContractPurgeRequest
            {
                Mode = DraftPurgeMode.Scheduled,
                TriggeredByUserId = SystemUserId
            };

            var result = await purgeService.ExecuteAsync(request, ct);

            if (result.Success)
            {
                _logger.LogInformation("Scheduled Draft Contract Purge completed. Deleted: {Deleted}, Failed: {Failed}",
                    result.DeletedCount, result.FailedCount);
            }
            else
            {
                _logger.LogWarning("Scheduled Draft Contract Purge completed with issues: {Error}", result.ErrorMessage);
            }
        }

        private async Task UpdateNextRunOnlyAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var process = await db.SchedulerProcesses
                .Where(p => p.Name == ProcessName && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (process != null)
            {
                process.NextRunDt = DraftContractPurgeService.GetNextRunAt0300(DateTime.Now);
                await db.SaveChangesAsync(ct);
            }
        }

        private async Task<bool> ProcessExistsAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await db.SchedulerProcesses
                .AsNoTracking()
                .AnyAsync(p => p.Name == ProcessName && !p.IsDeleted, ct);
        }

        private async Task LogEventToDbAsync(EventType eventType, string description, string? stackTrace, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await LoggerHelper.LogEventAsync(db, eventType, description, stackTrace, SystemUserId);
            }
            catch (Exception ex)
            {
                // Never let logging failures break the scheduler
                _logger.LogWarning(ex, "Failed to log event to database: {Description}", description);
            }
        }

        private static TimeSpan CalculateDelay(DateTime? nextRun, DateTime now)
        {
            if (!nextRun.HasValue)
            {
                // No scheduled run, check again in 1 hour
                return TimeSpan.FromHours(1);
            }

            var timeUntilRun = nextRun.Value - now;

            if (timeUntilRun <= TimeSpan.Zero)
            {
                // Already past scheduled time, run immediately (very short delay)
                return TimeSpan.FromMilliseconds(100);
            }

            // Don't wait too long - check at least every hour
            if (timeUntilRun > TimeSpan.FromHours(1))
            {
                return TimeSpan.FromHours(1);
            }

            // If less than a minute, check every 10 seconds
            if (timeUntilRun < TimeSpan.FromMinutes(1))
            {
                return TimeSpan.FromSeconds(10);
            }

            // Check at least every minute for precision
            return TimeSpan.FromMinutes(1);
        }
    }
}
