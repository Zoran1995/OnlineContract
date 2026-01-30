namespace OnlineContract.Services.Reports
{
    public class EomReportResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public List<ContractStatusSummary> Summaries { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }

        public EomReportStatus Status
        {
            get
            {
                if (!Success) return EomReportStatus.Failed;
                if (Warnings.Count > 0) return EomReportStatus.Warning;
                if (Summaries.All(s => s.Count == 0)) return EomReportStatus.SuccessfulNothingProcessed;
                return EomReportStatus.Successful;
            }
        }
    }

    public class ContractStatusSummary
    {
        public string StatusName { get; set; } = string.Empty;
        public int StatusId { get; set; }
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public enum EomReportStatus
    {
        Successful,
        Warning,
        Failed,
        SuccessfulNothingProcessed
    }
}
