using Microsoft.AspNetCore.Mvc;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;
using OnlineContract.Services.Reports;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly IEomReportService _eomReportService;
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ReportsController(
            IEomReportService eomReportService,
            AppDbContext db,
            IHostEnvironment env)
        {
            _eomReportService = eomReportService;
            _db = db;
            _env = env;
        }

        private int CurrentUserId() => UserContextHelper.GetCurrentUserId(HttpContext);
        private bool CanManageProducts() => UserContextHelper.CanManageProducts(HttpContext);

        /// <summary>
        /// Manually triggers the EOM Report generation.
        /// </summary>
        [HttpPost("eom/run")]
        public async Task<IActionResult> RunEomReport([FromBody] EomRunRequest? body, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var userId = CurrentUserId();
            if (userId <= 0)
            {
                return Unauthorized(new { message = "User not authenticated." });
            }

            try
            {
                var request = new EomReportRequest
                {
                    Mode = EomReportMode.Manual,
                    TriggeredByUserId = userId
                };

                // Execute the report (this will handle notifications and task creation)
                var result = await _eomReportService.ExecuteAsync(request, ct);

                if (!result.Success && result.ErrorMessage == "Process is already running.")
                {
                    return Conflict(new
                    {
                        message = "The EOM Report process is already running. Please wait for it to complete.",
                        status = "running"
                    });
                }

                return Ok(new
                {
                    success = result.Success,
                    status = result.Status.ToString(),
                    filePath = result.FilePath,
                    periodFrom = result.PeriodFrom,
                    periodTo = result.PeriodTo,
                    durationMs = result.Duration.TotalMilliseconds,
                    summaries = result.Summaries.Select(s => new
                    {
                        statusName = s.StatusName,
                        count = s.Count,
                        totalAmount = s.TotalAmount
                    }),
                    warnings = result.Warnings,
                    errorMessage = result.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Manual EOM Report trigger failed", ex.ToString(), userId);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Failed to execute EOM Report.",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Gets a preview of EOM report data for the specified period without generating a PDF.
        /// </summary>
        [HttpGet("eom/preview")]
        public async Task<IActionResult> GetEomPreview([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            if (!CanManageProducts())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                // Default to current month to now if not specified
                var (defaultFrom, defaultTo) = DateRangeHelper.GetCurrentMonthToNowWindow(DateTime.Now);
                var periodFrom = from ?? defaultFrom;
                var periodTo = to ?? defaultTo;

                var result = await _eomReportService.GetPreviewAsync(periodFrom, periodTo, ct);

                return Ok(new
                {
                    periodFrom = result.PeriodFrom,
                    periodTo = result.PeriodTo,
                    summaries = result.Summaries.Select(s => new
                    {
                        statusName = s.StatusName,
                        statusId = s.StatusId,
                        count = s.Count,
                        totalAmount = s.TotalAmount
                    }),
                    totalCount = result.Summaries.Sum(s => s.Count),
                    totalAmount = result.Summaries.Sum(s => s.TotalAmount),
                    warnings = result.Warnings
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "EOM Preview failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Failed to get EOM preview.",
                    error = ex.Message
                });
            }
        }
    }

    public class EomRunRequest
    {
        public string Mode { get; set; } = "Manual";
    }
}
