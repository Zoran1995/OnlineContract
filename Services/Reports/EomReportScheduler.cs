using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlineContract.Data;
using OnlineContract.Helpers;

namespace OnlineContract.Services.Reports
{
    /// <summary>
    /// Background service that schedules EOM Report generation on the 1st of each month at 00:00.
    /// </summary>
    public class EomReportScheduler : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EomReportScheduler> _logger;

        private const string ProcessName = "EOM Report Generation";
        private const int SystemUserId = 2;

        public EomReportScheduler(IServiceProvider serviceProvider, ILogger<EomReportScheduler> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EOM Report Scheduler started.");
            await LogEventToDbAsync(EventType.Information, 
                $"[Scheduler] {ProcessName} scheduler started. Waiting for scheduled execution time.", 
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
                            _logger.LogError("EOM Report process not found in database, cannot execute.");
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
                            _logger.LogInformation("Executing scheduled EOM Report generation for {Date}", now);
                            await LogEventToDbAsync(EventType.Information, 
                                $"[Scheduler] {ProcessName} - Scheduled execution triggered at {now:yyyy-MM-dd HH:mm:ss}. Mode: Scheduled.", 
                                null, stoppingToken);
                            await ExecuteReportAsync(stoppingToken);
                        }
                        else
                        {
                            _logger.LogInformation("EOM Report process is not active, skipping scheduled run.");
                            await LogEventToDbAsync(EventType.Warning, 
                                $"[Scheduler] {ProcessName} - SKIPPED: Process is deactivated (is_active = 0). " +
                                $"Scheduled run at {now:yyyy-MM-dd HH:mm:ss} was not executed. " +
                                "Reason: An administrator has deactivated this process.", 
                                null, stoppingToken);
                            // Still update next_run_dt to avoid repeated checks
                            await UpdateNextRunOnlyAsync(stoppingToken);
                        }
                    }

                    // Wait until next check (every minute, or until next run time)
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
                    _logger.LogError(ex, "Error in EOM Report Scheduler loop");
                    await LogEventToDbAsync(EventType.Error, 
                        $"[Scheduler] {ProcessName} - ERROR IN SCHEDULER LOOP: {ex.Message}. " +
                        "Reason: An unexpected error occurred in the background scheduler. The scheduler will retry in 1 minute.", 
                        ex.ToString(), stoppingToken);
                    // Wait a bit before retrying on error
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("EOM Report Scheduler stopped.");
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

        private async Task ExecuteReportAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IEomReportService>();

            var request = new EomReportRequest
            {
                Mode = EomReportMode.Scheduled,
                TriggeredByUserId = SystemUserId
            };

            var result = await reportService.ExecuteAsync(request, ct);

            if (result.Success)
            {
                _logger.LogInformation("Scheduled EOM Report completed successfully. File: {FilePath}", result.FilePath);
            }
            else
            {
                _logger.LogWarning("Scheduled EOM Report completed with issues: {Error}", result.ErrorMessage);
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
                process.NextRunDt = Helpers.DateRangeHelper.GetNextRunDt(DateTime.Now);
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
                return TimeSpan.FromSeconds(1);
            }

            // Don't wait more than 1 hour at a time
            if (timeUntilRun > TimeSpan.FromHours(1))
            {
                return TimeSpan.FromHours(1);
            }

            return timeUntilRun;
        }
    }
}
