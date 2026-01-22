using System;

namespace OnlineContract.Models
{
    public class TaskItem
    {
        public int Id { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public DateTime InputDt { get; set; }
        public int AssignedToUserId { get; set; }
        public int InitiatedByUserId { get; set; }
        public int Priority { get; set; }
        public int Status { get; set; }
        public DateTime? ReminderDt { get; set; }
        public int? ContractId { get; set; }
        public DateTime? CompletedDt { get; set; }
        public int Stamp { get; set; }
    }
}