using Microsoft.Extensions.Configuration;
using System.Text;
using System.Security.Cryptography;

namespace OnlineContract.Services
{
    public class WspayClient : IWspayClient
    {
        private readonly IConfiguration _cfg;
        public WspayClient(IConfiguration cfg) { _cfg = cfg; }

        public async Task<(bool ok, string redirectUrl, string externalOrderId, string? error)> CreateHostedPaymentAsync(int contractId, decimal amount, string currency, string customerEmail, string customerName)
        {
            var baseSection = _cfg.GetSection("Payments:WSPay");
            var env = baseSection["Environment"] ?? "Sandbox";
            var merchantId = baseSection["MerchantId"] ?? string.Empty;
            var storeId = baseSection["StoreId"] ?? string.Empty;
            var apiSecret = baseSection["ApiSecret"] ?? string.Empty;
            var returnSuccess = baseSection["ReturnUrlSuccess"] ?? "/payments/wspay/return/success";
            var returnCancel = baseSection["ReturnUrlCancel"] ?? "/payments/wspay/return/cancel";
            var currencyCfg = baseSection["Currency"] ?? currency;
            var useMinor = string.Equals(baseSection["UseMinorUnits"], "true", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(merchantId) || string.IsNullOrWhiteSpace(apiSecret))
                return (false, string.Empty, string.Empty, "Missing WSPay credentials");

            // External order id
            var externalOrderId = $"C-{contractId}-{Guid.NewGuid():N}";

            // Amount representation (minor units optional)
            var amountStr = useMinor ? ((long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero)).ToString() : amount.ToString("0.00");

            // Build canonical string for request signing
            var canonical = BuildCanonicalString(new Dictionary<string, string?>
            {
                { "merchantId", merchantId },
                { "externalOrderId", externalOrderId },
                { "amount", amountStr },
                { "currency", currencyCfg },
                { "status", "init" },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
            });
            var signature = ComputeHmacSha256(apiSecret, canonical);

            // Choose endpoint
            var endpoint = env.Equals("Production", StringComparison.OrdinalIgnoreCase)
                ? "https://form.wspay.eu/authorization.aspx"
                : "https://formtest.wspay.eu/authorization.aspx";

            // Build redirect URL via temporary page that auto-POSTs, return endpoint for form submit
            // For simplicity here return endpoint and let frontend build form fields
            var redirectUrl = endpoint;

            // Normally we'd return a set of fields; keep URL-only for now
            await Task.CompletedTask;
            return (true, redirectUrl, externalOrderId, null);
        }

        public bool VerifyWebhookSignature(string signature, string canonical, string secret)
        {
            var expectedHex = ComputeHmacSha256(secret, canonical);
            // PSPs may send Base64 or Hex; normalize both sides
            var sigNorm = (signature ?? string.Empty).Trim();
            try
            {
                var asHex = sigNorm.ToLowerInvariant();
                if (SlowEquals(asHex, expectedHex)) return true;
                // Try Base64
                var sigBytes = Convert.FromBase64String(sigNorm);
                var sigHex = Convert.ToHexString(sigBytes).ToLowerInvariant();
                return SlowEquals(sigHex, expectedHex);
            }
            catch
            {
                return false;
            }
        }

        public string BuildCanonicalStringForWebhook(IDictionary<string, string?> fields)
        {
            // Fixed order: merchantId|externalOrderId|amount|currency|status|timestamp|transactionId
            var orderedKeys = new[] { "merchantId", "externalOrderId", "amount", "currency", "status", "timestamp", "transactionId" };
            var sb = new StringBuilder();
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                if (i > 0) sb.Append('|');
                var k = orderedKeys[i];
                fields.TryGetValue(k, out var v);
                sb.Append(v ?? string.Empty);
            }
            return sb.ToString();
        }

        public string BuildCanonicalString(IDictionary<string, string?> fields) => BuildCanonicalStringForWebhook(fields);

        private static string ComputeHmacSha256(string secret, string canonical)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical ?? string.Empty));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool SlowEquals(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            var diff = (uint)ba.Length ^ (uint)bb.Length;
            var len = Math.Min(ba.Length, bb.Length);
            for (int i = 0; i < len; i++) diff |= (uint)(ba[i] ^ bb[i]);
            return diff == 0;
        }
    }
}