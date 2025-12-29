using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.WebUtilities;

namespace OnlineContract.Helpers
{
    /// <summary>
    /// Adapter from new Services.IEmailService to the existing Helpers.IEmailService used in codebase.
    /// </summary>
    public class EmailServiceAdapter : IEmailService
    {
        private readonly OnlineContract.Services.IEmailService _svc;

        public EmailServiceAdapter(OnlineContract.Services.IEmailService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        public async Task<(bool success, string? error)> SendResetEmailAsync(IConfiguration config, string toEmail, string resetLink)
        {
            try
            {
                // extract token from resetLink if possible
                string? token = null;
                try
                {
                    var idx = resetLink.IndexOf('?');
                    if (idx >= 0)
                    {
                        var q = QueryHelpers.ParseQuery(resetLink.Substring(idx));
                        if (q.TryGetValue("token", out var v)) token = v.ToString();
                    }
                }
                catch { }

                await _svc.SendResetEmailAsync(toEmail, null, token);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
