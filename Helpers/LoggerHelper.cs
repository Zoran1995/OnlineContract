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

            var log = new EventLog
            {
                EventTypeId = (int)type,
                InputDt = DateTime.Now,
                Description = safeDescription,
                StackTrace = safeStack,
                UserId = validUser ? userId : 2,
                Stamp = 0
            };

            db.EventLogs.Add(log);
            await db.SaveChangesAsync();
        }
    }
}