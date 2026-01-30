namespace OnlineContract.Services.DraftPurge
{
    /// <summary>
    /// Request model for triggering the Draft Contract Purge process.
    /// </summary>
    public class DraftContractPurgeRequest
    {
        /// <summary>
        /// Whether this is a scheduled or manual run.
        /// </summary>
        public DraftPurgeMode Mode { get; set; } = DraftPurgeMode.Manual;

        /// <summary>
        /// The user ID that triggered the manual run, or System (2) for scheduled runs.
        /// </summary>
        public int TriggeredByUserId { get; set; } = 2;
    }

    public enum DraftPurgeMode
    {
        Manual,
        Scheduled
    }
}
