namespace OnlineContract.Models
{
    public class SchedulerProcess
    {
        public int Id { get; set; }              // scheduler_process_id
        public string? Name { get; set; }
        public string? Description { get; set; }
        public DateTime? LastEndDt { get; set; }
        public DateTime? LastStartDt { get; set; }
        public DateTime? NextRunDt { get; set; }
        public int? Duration { get; set; }
        public int Status { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public int Stamp { get; set; }
    }
}