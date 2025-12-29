using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OnlineContract.Helpers
{
    public class EmailService : IEmailService
    {
        public Task<(bool success, string? error)> SendResetEmailAsync(IConfiguration config, string toEmail, string resetLink)
        {
            return EmailHelper.SendResetEmailAsync(config, toEmail, resetLink);
        }
    }
}
