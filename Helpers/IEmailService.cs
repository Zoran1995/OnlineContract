using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OnlineContract.Helpers
{
    public interface IEmailService
    {
        Task<(bool success, string? error)> SendResetEmailAsync(IConfiguration config, string toEmail, string resetLink);
    }
}
