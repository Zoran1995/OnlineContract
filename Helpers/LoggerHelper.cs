using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using System.Net;

namespace OnlineContract.Helpers
{
    public static class LoggerHelper
    {
        public static async Task LogEventAsync(
            AppDbContext db,
            EventType type,
            string description,
            string? stackTrace,
            int userId)
        {
            var validUser = await db.AxUsers.AnyAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted);

            // Sanitize/encode inputs to avoid storing raw HTML/JS
            var safeDescription = WebUtility.HtmlEncode(description ?? "");
            var safeStack = stackTrace is null ? null : WebUtility.HtmlEncode(stackTrace);

            try
            {
                // Use direct SQL insert to avoid EF OUTPUT clause issues on tables with triggers.
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.event_log (event_type, input_dt, description, stack_trace, user_id, stamp)
                    VALUES ({(int)type}, {DateTime.Now}, {safeDescription}, {safeStack}, {(validUser ? userId : 2)}, 0);");
            }
            catch
            {
                // Logging should never break the main request flow; swallow failures.
            }
        }
    }
}