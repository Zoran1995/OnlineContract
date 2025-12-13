using System;
using Microsoft.EntityFrameworkCore;

namespace OnlineContract.Models
{
    [Keyless]
    public class EventLogView
    {
        public int EventLogId { get; set; }
        public int EventTypeId { get; set; }
        public string EventTypeText { get; set; } = string.Empty;
        public DateTime InputDt { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public int UserId { get; set; }
        public string UserCode { get; set; } = string.Empty;
    }
}