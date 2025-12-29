using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OnlineContract.Helpers
{
    public static class EmailHelper
    {
        // Returns (success, errorMessage)
        public static async Task<(bool success, string? error)> SendResetEmailAsync(IConfiguration config, string toEmail, string resetLink)
        {
            try
            {
                var smtpHost = config["Smtp:Host"] ?? "smtp.gmail.com";
                var smtpPort = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
                var smtpUser = config["Smtp:User"];
                var smtpPass = config["Smtp:Pass"];
                // Prefer environment variable if config value not provided (keep secrets out of repo)
                if (string.IsNullOrWhiteSpace(smtpPass))
                {
                    smtpPass = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? string.Empty;
                }
                var appName = config["AppName"] ?? "App";
                var from = config["Smtp:From"] ?? ("noreply." + (config["AppName"] ?? "app") + "@gmail.com");

                var msg = new MailMessage();
                msg.From = new MailAddress(from, appName + " Support");
                msg.To.Add(new MailAddress(toEmail));
                msg.Subject = "Password reset instructions";

                msg.IsBodyHtml = true;
                var sb = new System.Text.StringBuilder();
                sb.Append("<p>You requested to reset your password for your account at ");
                sb.Append(WebUtility.HtmlEncode(appName));
                sb.Append(".</p>");
                sb.Append("<p>If you did not initiate this request, please contact our administrator immediately.</p>");
                sb.Append("<p>To reset your password, click the link below. This link will expire in 15 minutes and can be used only once:<br/>");
                sb.Append("<a href=\"");
                sb.Append(WebUtility.HtmlEncode(resetLink));
                sb.Append("\">Reset your password</a></p>");
                sb.Append("<p>Thank you,<br/>");
                sb.Append(WebUtility.HtmlEncode(appName));
                sb.Append(" Support</p>");
                msg.Body = sb.ToString();

                using (var client = new SmtpClient(smtpHost, smtpPort))
                {
                    client.EnableSsl = true;
                    if (!string.IsNullOrWhiteSpace(smtpUser) && !string.IsNullOrWhiteSpace(smtpPass))
                    {
                        client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                    }
                    else
                    {
                        // Missing credentials — return an error so caller can log and avoid silent failures
                        return (false, "SMTP credentials missing (Smtp:User or Smtp:Pass / SMTP_PASSWORD not set)");
                    }
                    await Task.Run(() => client.Send(msg));
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.ToString());
            }
        }
    }
}
