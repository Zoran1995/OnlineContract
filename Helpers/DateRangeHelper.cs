namespace OnlineContract.Helpers
{
    /// <summary>
    /// Helper methods for date range calculations used by the EOM Report feature.
    /// Uses half-open intervals: [from, toExclusive) to avoid datetime precision issues.
    /// </summary>
    public static class DateRangeHelper
    {
        /// <summary>
        /// Gets the date range for the previous month using half-open interval.
        /// From: First day of previous month at 00:00:00.000 (inclusive)
        /// ToExclusive: First day of current month at 00:00:00.000 (exclusive)
        /// SQL: WHERE dt >= @from AND dt &lt; @toExclusive
        /// </summary>
        public static (DateTime From, DateTime ToExclusive) GetPreviousMonthWindow(DateTime now)
        {
            var firstOfCurrentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Local);
            var firstOfPreviousMonth = firstOfCurrentMonth.AddMonths(-1);
            
            return (firstOfPreviousMonth, firstOfCurrentMonth);
        }

        /// <summary>
        /// Gets the date range from the first day of the current month to now using half-open interval.
        /// From: First day of current month at 00:00:00.000 (inclusive)
        /// ToExclusive: Now (exclusive)
        /// SQL: WHERE dt >= @from AND dt &lt; @toExclusive
        /// </summary>
        public static (DateTime From, DateTime ToExclusive) GetCurrentMonthToNowWindow(DateTime now)
        {
            var firstOfCurrentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Local);
            return (firstOfCurrentMonth, now);
        }

        /// <summary>
        /// Gets the next run date (first day of next month at 00:00).
        /// </summary>
        public static DateTime GetNextRunDt(DateTime now)
        {
            var firstOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
            var firstOfNextMonth = firstOfCurrentMonth.AddMonths(1);
            return firstOfNextMonth;
        }

        /// <summary>
        /// Formats a TimeSpan as a human-readable duration string.
        /// </summary>
        public static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds < 60)
            {
                return $"{duration.TotalSeconds:F1}s";
            }
            if (duration.TotalMinutes < 60)
            {
                return $"{duration.Minutes}m {duration.Seconds}s";
            }
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }
    }
}
