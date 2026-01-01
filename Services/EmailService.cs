using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace OnlineContract.Services
{
    /// <summary>
    /// SMTP email sender using Gmail credentials from environment variables.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly string _user;
        private readonly string _pass;
        private readonly string _appOriginFull;

        public EmailService(ILogger<EmailService> logger, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _logger = logger;
            // Prefer configuration (user-secrets / appsettings) for local dev, fallback to environment variables
            _user = config["GMAIL_USER"] ?? Environment.GetEnvironmentVariable("GMAIL_USER") ?? string.Empty;
            _pass = config["GMAIL_APP_PASSWORD"] ?? Environment.GetEnvironmentVariable("GMAIL_APP_PASSWORD") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_user) || string.IsNullOrWhiteSpace(_pass))
            {
                _logger.LogWarning("Email credentials not found in configuration or environment variables. Email sending will fail until configured.");
            }

            // Resolve application origin for reset links. Preference order:
            // 1) configuration key "AppOrigin"
            // 2) environment variable "APP_ORIGIN"
            // 3) ASPNETCORE_URLS (first entry)
            // 4) fallback to https://localhost:52616 (developer-friendly default)
            string origin = config["AppOrigin"] ?? Environment.GetEnvironmentVariable("APP_ORIGIN") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(origin))
            {
                var urls = config["ASPNETCORE_URLS"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(urls)) origin = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
            }
            if (string.IsNullOrWhiteSpace(origin)) origin = "http://localhost:5261";
            // Ensure scheme present and prefer http (user requested links without S)
            if (!origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                origin = "http://" + origin.Trim();
            }
            // If origin was https, convert to http to produce links without S
            if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                origin = "http://" + origin.Substring(8);
            }
            // If multiple urls separated by ;, keep first
            if (origin.Contains(';')) origin = origin.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
            // Normalize
            try { var u = new Uri(origin); _appOriginFull = u.GetLeftPart(UriPartial.Authority); }
            catch { _appOriginFull = origin.TrimEnd('/'); }
        }

        /// <summary>
        /// Send a reset email either as a short code or a token link.
        /// </summary>
        public async Task SendResetEmailAsync(string to, string? code = null, string? token = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Either code or token must be provided.");

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_user));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = "Password reset instructions";

            var builder = new BodyBuilder();

            if (!string.IsNullOrWhiteSpace(code))
            {
                // OTP scenario
                builder.TextBody = $"Your password reset code is: {code}. Code expires in 15 minutes.";
                builder.HtmlBody = $"<p>Your password reset code is: <strong>{code}</strong></p><p>Code expires in 15 minutes.</p>";
            }
            else
            {
                // Token/link scenario — make the email copy more descriptive and friendly
                    var link = token != null ? $"{_appOriginFull.TrimEnd('/')}/reset?token={Uri.EscapeDataString(token)}" : "";
                builder.TextBody =
$"You (or someone using this email address) recently requested to reset the password for your Online Contracts account.\n\n" +
"To choose a new password, open the link below within 15 minutes:\n\n" +
$"{link}\n\n" +
                    $"If the link does not open, copy and paste the full URL into your browser. If you did not request a password reset, you can safely ignore this message — no changes will be made to your account.\n\n" +
                    $"This is an automated email - please do not reply to this message. If you need help, contact your administrator.";

                builder.HtmlBody =
                    $"<div style=\"font-family:Arial,Helvetica,sans-serif;color:#222;line-height:1.4\">" +
                    $"<h2 style=\"color:#1f2937;margin:0 0 8px\">Password reset requested</h2>" +
                    $"<p style=\"margin:0 0 12px\">You (or someone using this email address) recently requested to reset the password for your <strong>Online Contracts</strong> account.</p>" +
                    $"<p style=\"margin:0 0 12px\">To choose a new password, click the button below within <strong>15 minutes</strong>:</p>" +
                    $"<p style=\"margin:0 0 18px\"><a href=\"{link}\" style=\"display:inline-block;padding:10px 16px;background:#2563eb;color:#fff;border-radius:6px;text-decoration:none\">Reset password</a></p>" +
                    $"<p style=\"margin:0 0 8px;font-size:13px;color:#444\">If the button does not work, copy and paste this URL into your browser:</p>" +
                    $"<p style=\"word-break:break-all;font-size:13px;color:#0b1220\">{link}</p>" +
                    $"<hr style=\"border:none;border-top:1px solid #eee;margin:18px 0\">" +
                    $"<p style=\"font-size:12px;color:#6b7280;margin:0\">If you did not request a password reset, please ignore this email. No changes will be made to your account.</p>" +
                        $"<p style=\"font-size:12px;color:#6b7280;margin:8px 0 0\"><em>This is an automated email — please do not reply.</em></p>" +
                        $"</div>";
            }

            message.Body = builder.ToMessageBody();

            Exception? lastEx = null;
            try
            {
                using var client = new SmtpClient();
                // Try SSL on 465 first
                await client.ConnectAsync("smtp.gmail.com", 465, SecureSocketOptions.SslOnConnect, ct);
                await client.AuthenticateAsync(_user, _pass, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);

                _logger.LogInformation("Sent password reset email to {RecipientDomain} using port 465", MaskDomain(to));
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex465)
            {
                lastEx = ex465;
                _logger.LogWarning(ex465, "Port 465 send failed, will try port 587 StartTLS");
            }

            try
            {
                using var client = new SmtpClient();
                // Fallback to STARTTLS on 587
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
                await client.AuthenticateAsync(_user, _pass, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);

                _logger.LogInformation("Sent password reset email to {RecipientDomain} using port 587", MaskDomain(to));
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex587)
            {
                _logger.LogError(ex587, "Failed to send reset email to {RecipientDomain} on both ports", MaskDomain(to));
                // throw the last exception for callers to handle
                throw lastEx ?? ex587;
            }
        }

        /// <summary>
        /// Health check: connect, authenticate, NOOP (via Noop or just connect/authenticate), disconnect.
        /// </summary>
        public async Task<bool> HealthAsync(CancellationToken ct = default)
        {
            try
            {
                using var client = new SmtpClient();
                await client.ConnectAsync("smtp.gmail.com", 465, SecureSocketOptions.SslOnConnect, ct);
                await client.AuthenticateAsync(_user, _pass, ct);
                // MailKit does not expose NOOP on SmtpClient directly; Send an empty command by checking IsAuthenticated
                await client.DisconnectAsync(true, ct);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMTP health check failed");
                return false;
            }
        }

        // GetAppDomain removed; EmailService now uses the configured _appOriginFull set in the constructor.

        private static string MaskDomain(string email)
        {
            try
            {
                var at = email.IndexOf('@');
                if (at <= 0) return email;
                return "***@" + email.Substring(at + 1);
            }
            catch { return email; }
        }
    }
}
