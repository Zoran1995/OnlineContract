using Microsoft.Extensions.Configuration;

namespace OnlineContract.Services
{
    public class StubWspayClient : IWspayClient
    {
        private readonly IConfiguration _cfg;
        public StubWspayClient(IConfiguration cfg) { _cfg = cfg; }

        public Task<(bool ok, string redirectUrl, string externalOrderId, string? error)> CreateHostedPaymentAsync(int contractId, decimal amount, string currency, string customerEmail, string customerName)
        {
            var externalOrderId = $"C-{contractId}-{Guid.NewGuid():N}";
            var redirectUrl = $"/payments/wspay/mock?reference={Uri.EscapeDataString(externalOrderId)}&contractId={contractId}";
            return Task.FromResult((true, redirectUrl, externalOrderId, (string?)null));
        }

        public bool VerifyWebhookSignature(string signature, string canonical, string secret)
        {
            // In non-production stub, signature is not required; accept all
            return true;
        }

        public string BuildCanonicalStringForWebhook(IDictionary<string, string?> fields)
        {
            // Minimal canonical for stub; order not critical
            var order = new[] { "merchantId", "externalOrderId", "amount", "currency", "status", "timestamp", "transactionId" };
            return string.Join('|', order.Select(k => fields.TryGetValue(k, out var v) ? (v ?? string.Empty) : string.Empty));
        }
    }
}
