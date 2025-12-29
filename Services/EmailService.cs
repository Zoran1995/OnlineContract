using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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
                // Token/link scenario
                var link = token != null ? $"https://{GetAppDomain()}/reset?token={Uri.EscapeDataString(token)}" : "";
                builder.TextBody = $"Open the following link to reset your password:\n{link}\nIf the link is not clickable, copy-paste it into your browser.";
                builder.HtmlBody = $"<p>Click the link to reset your password:</p><p><a href=\"{link}\">Reset password</a></p><p>If the link does not work, copy-paste this URL into your browser:</p><p>{link}</p>";
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

        private static string GetAppDomain()
        {
            // Prefer ASPNETCORE_... or fallback to localhost: use env var APP_ORIGIN if present
            var origin = Environment.GetEnvironmentVariable("APP_ORIGIN");
            if (!string.IsNullOrWhiteSpace(origin))
            {
                try
                {
                    var u = new Uri(origin);
                    return u.Host + (u.IsDefaultPort ? "" : ":" + u.Port);
                }
                catch { }
            }
            return "localhost";
        }

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
