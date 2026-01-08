namespace OnlineContract.Services
{
    /// <summary>
    /// Email service for sending password reset codes or links and checking SMTP health.
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Send a password reset email. Provide either <paramref name="code"/> or <paramref name="token"/>.
        /// </summary>
        Task SendResetEmailAsync(string to, string? code = null, string? token = null, CancellationToken ct = default);

        /// <summary>
        /// Perform a lightweight SMTP connectivity/authentication check.
        /// </summary>
        Task<bool> HealthAsync(CancellationToken ct = default);
    }
}
