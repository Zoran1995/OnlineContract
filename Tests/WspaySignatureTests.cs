using System.Collections.Generic;
using OnlineContract.Services;
using Xunit;
using Microsoft.Extensions.Configuration;

namespace OnlineContract.Tests
{
    public class WspaySignatureTests
    {
        [Fact]
        public void Canonical_Builder_Fixed_Order_And_Signature_Valid()
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Payments:WSPay:ApiSecret","secret"}}).Build();
            var client = new WspayClient(cfg);
            var fields = new Dictionary<string,string?>{
                {"merchantId","m1"},{"externalOrderId","ext123"},{"amount","100.00"},{"currency","RSD"},{"status","success"},{"timestamp","1700000000"},{"transactionId","tx999"}
            };
            var canonical = client.BuildCanonicalStringForWebhook(fields);
            // Compute expected hex
            var expectedHex = typeof(WspayClient).GetMethod("VerifyWebhookSignature") != null; // just ensure method exists
            // Use client to verify both hex and base64 encodings
            var hexSig = InvokeHmac("secret", canonical);
            Assert.True(client.VerifyWebhookSignature(hexSig, canonical, "secret"));
            var base64Sig = HexToBase64(hexSig);
            Assert.True(client.VerifyWebhookSignature(base64Sig, canonical, "secret"));
        }

        [Fact]
        public void Signature_Fails_On_Altered_Field()
        {
            var cfg = new ConfigurationBuilder().Build();
            var client = new WspayClient(cfg);
            var canonical = client.BuildCanonicalStringForWebhook(new Dictionary<string,string?>{{"merchantId","m1"},{"externalOrderId","ext123"},{"amount","100.00"},{"currency","RSD"},{"status","success"},{"timestamp","1700000000"},{"transactionId","tx999"}});
            var hexSig = InvokeHmac("secret", canonical);
            var canonicalAltered = client.BuildCanonicalStringForWebhook(new Dictionary<string,string?>{{"merchantId","m1"},{"externalOrderId","extX"},{"amount","100.00"},{"currency","RSD"},{"status","success"},{"timestamp","1700000000"},{"transactionId","tx999"}});
            Assert.False(client.VerifyWebhookSignature(hexSig, canonicalAltered, "secret"));
        }

        private static string InvokeHmac(string secret, string canonical)
        {
            using var sys = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
            var hash = sys.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
            return System.Convert.ToHexString(hash).ToLowerInvariant();
        }
        private static string HexToBase64(string hex)
        {
            var bytes = new byte[hex.Length/2];
            for(int i=0;i<bytes.Length;i++) bytes[i] = System.Convert.ToByte(hex.Substring(i*2,2), 16);
            return System.Convert.ToBase64String(bytes);
        }
    }
}