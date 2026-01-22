using System.Threading.Tasks;

namespace OnlineContract.Services
{
    public interface IWspayClient
    {
        Task<(bool ok, string redirectUrl, string externalOrderId, string? error)> CreateHostedPaymentAsync(int contractId, decimal amount, string currency, string customerEmail, string customerName);
        bool VerifyWebhookSignature(string signature, string canonical, string secret);
        string BuildCanonicalStringForWebhook(IDictionary<string, string?> fields);
    }
}