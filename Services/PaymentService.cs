using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OnlineContract.Data;
using OnlineContract.Models;

namespace OnlineContract.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly AppDbContext _db;
        private readonly IWspayClient _wspay;
        private readonly IConfiguration _cfg;

        public PaymentService(AppDbContext db, IWspayClient wspay, IConfiguration cfg)
        {
            _db = db; _wspay = wspay; _cfg = cfg;
        }

        public async Task<(bool ok, string redirectUrl, string externalOrderId, string? error)> CreatePaymentIntentAsync(int contractId, decimal amount, CustomerInfo info)
        {
            var currency = _cfg["Payments:WSPay:Currency"] ?? "RSD";
            // Ensure per-line amounts are rounded to 2 decimals before header sum when computing outgoing amount (AmtGross already computed server-side)
            var outgoingAmount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            var (ok, url, extId, err) = await _wspay.CreateHostedPaymentAsync(contractId, outgoingAmount, currency, info.Email, info.FullName);
            if (!ok) return (false, string.Empty, string.Empty, err);

            // Persist Payment (Pending)
            var p = new Payment
            {
                ContractId = contractId,
                Provider = "WSPay",
                ExternalOrderId = extId,
                AmountGross = outgoingAmount,
                Currency = currency,
                Status = "Pending",
                CreatedDt = DateTime.Now,
                Stamp = 0
            };
            _db.Payments.Add(p);
            await _db.SaveChangesAsync();
            return (true, url, extId, null);
        }

        public async Task<PaymentCallbackResult> HandleCallbackAsync(IDictionary<string, string> fields)
        {
            // Expect fields to include status and external_order_id; tests may instead send contractId
            var status = fields.TryGetValue("status", out var s) ? (s ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
            var ext = fields.TryGetValue("external_order_id", out var eo) ? (eo ?? string.Empty) : string.Empty;
            var txid = fields.TryGetValue("transaction_id", out var ti) ? (ti ?? string.Empty) : string.Empty;
            var amountStr = fields.TryGetValue("amount", out var am) ? (am ?? string.Empty) : string.Empty;
            var currencyStr = fields.TryGetValue("currency", out var cu) ? (cu ?? string.Empty) : string.Empty;
            var cidStr = fields.TryGetValue("contractId", out var cidVal) ? cidVal : string.Empty;
            int contractId = 0;
            if (!string.IsNullOrEmpty(cidStr)) int.TryParse(cidStr, out contractId);

            Payment? payment = null;
            if (!string.IsNullOrEmpty(ext))
            {
                payment = await _db.Payments.FirstOrDefaultAsync(p => p.ExternalOrderId == ext);
                if (payment != null) contractId = payment.ContractId;
            }
            if (payment == null && contractId > 0)
            {
                payment = await _db.Payments
                    .OrderByDescending(p => p.CreatedDt)
                    .FirstOrDefaultAsync(p => p.ContractId == contractId && p.Status == "Pending");
            }

            var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && !c.IsDeleted);
            if (contract == null) return new PaymentCallbackResult(false, false, "Contract not found");

            if (status == "success")
            {
                // Idempotent: check contract submitted and matched
                if (contract.ContractState == Helpers.ContractState.Submitted && contract.AmtMatched >= contract.Amount)
                    return new PaymentCallbackResult(true, true, null);

                // Amount/currency mismatch guard
                if (payment != null)
                {
                    payment.LastCallbackDt = DateTime.Now;
                    payment.LastCallbackStatus = status;
                    payment.TransactionId = string.IsNullOrEmpty(txid) ? payment.TransactionId : txid;

                    if (!string.IsNullOrEmpty(amountStr) && !string.IsNullOrEmpty(currencyStr))
                    {
                        var useMinor = string.Equals(_cfg["Payments:WSPay:UseMinorUnits"], "true", StringComparison.OrdinalIgnoreCase);
                        var expectedAmtStr = useMinor ? ((long)Math.Round(payment.AmountGross * 100m, 0, MidpointRounding.AwayFromZero)).ToString() : payment.AmountGross.ToString("0.00");
                        var amtMatches = string.Equals(amountStr, expectedAmtStr, StringComparison.OrdinalIgnoreCase);
                        var curMatches = string.Equals(currencyStr, payment.Currency, StringComparison.OrdinalIgnoreCase);
                        if (!amtMatches || !curMatches)
                        {
                            payment.Status = "Failed";
                            payment.UpdatedDt = DateTime.Now;
                            payment.Stamp = payment.Stamp + 1;
                            await _db.SaveChangesAsync();
                            return new PaymentCallbackResult(false, false, "Payment amount/currency mismatch.");
                        }
                    }
                }

                // Update items to Submitted
                var dets = await _db.ContractDets.Where(d => d.ContractId == contractId && !d.IsDeleted).ToListAsync();
                foreach (var d in dets)
                {
                    d.ItemStateId = Helpers.ProductStateInOrder.Submitted;
                    d.LastUpdatedDt = DateTime.Now;
                    d.Stamp = d.Stamp + 1;
                }
                // Recompute header amount via AmtGross sum (active/not-deleted)
                var newAmount = await _db.ContractDets.AsNoTracking()
                    .Where(d => d.ContractId == contractId && !d.IsDeleted)
                    .Select(d => (decimal?)d.AmtGross).SumAsync() ?? 0m;
                contract.Amount = newAmount;
                contract.ContractState = Helpers.ContractState.Submitted;
                contract.AmtMatched = contract.Amount;
                contract.LastUpdatedDt = DateTime.Now;
                contract.Stamp = contract.Stamp + 1;

                if (payment != null)
                {
                    payment.Status = "Succeeded";
                    payment.UpdatedDt = DateTime.Now;
                    payment.Stamp = payment.Stamp + 1;
                    payment.LastCallbackDt = DateTime.Now;
                    payment.LastCallbackStatus = status;
                    payment.TransactionId = string.IsNullOrEmpty(txid) ? payment.TransactionId : txid;
                }
                await _db.SaveChangesAsync();
                return new PaymentCallbackResult(true, false, null);
            }
            else if (status == "failure")
            {
                if (payment != null)
                {
                    payment.Status = "Failed";
                    payment.UpdatedDt = DateTime.Now;
                    payment.Stamp = payment.Stamp + 1;
                    payment.LastCallbackDt = DateTime.Now;
                    payment.LastCallbackStatus = status;
                    payment.TransactionId = string.IsNullOrEmpty(txid) ? payment.TransactionId : txid;
                    await _db.SaveChangesAsync();
                }
                return new PaymentCallbackResult(false, false, "Payment failed. Please try again.");
            }

            return new PaymentCallbackResult(false, false, "Unknown status");
        }
    }
}