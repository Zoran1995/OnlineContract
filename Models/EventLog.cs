using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("event_log")]
    public class EventLog
    {
        [Key]
        [Column("event_log_id")]
        public int EventLogId { get; set; }

        [Column("event_type")]
        public int EventTypeId { get; set; }

        [Column("input_dt")]
        public DateTime InputDt { get; set; }

        [Column("description")]
        public string Description { get; set; } = string.Empty;

        [Column("stack_trace")]
        public string? StackTrace { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; } = 0;
    }
}