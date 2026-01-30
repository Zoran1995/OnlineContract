namespace OnlineContract.Services.DraftPurge
{
    /// <summary>
    /// Result model for the Draft Contract Purge process.
    /// </summary>
    public class DraftContractPurgeResult
    {
        /// <summary>
        /// Whether the process completed without fatal errors.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Number of contracts successfully deleted.
        /// </summary>
        public int DeletedCount { get; set; }

        /// <summary>
        /// Number of contracts that failed to delete.
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Error message if the process failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Duration of the process.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Final status of the process.
        /// </summary>
        public DraftPurgeStatus Status
        {
            get
            {
                if (!Success) return DraftPurgeStatus.Failed;
                if (DeletedCount == 0 && FailedCount == 0) return DraftPurgeStatus.SuccessfulNothingProcessed;
                if (FailedCount > 0 && DeletedCount > 0) return DraftPurgeStatus.Warning;
                if (FailedCount > 0 && DeletedCount == 0) return DraftPurgeStatus.Failed;
                return DraftPurgeStatus.Successful;
            }
        }
    }

    public enum DraftPurgeStatus
    {
        Successful,
        Warning,
        Failed,
        SuccessfulNothingProcessed
    }
}
