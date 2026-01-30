namespace OnlineContract.Services.Reports
{
    public class EomReportRequest
    {
        public EomReportMode Mode { get; set; } = EomReportMode.Manual;
        public int TriggeredByUserId { get; set; }
    }

    public enum EomReportMode
    {
        Scheduled,
        Manual
    }
}
