using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;
using OnlineContract.Services;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _cfg;
        private readonly IWspayClient _wspClient;
        private readonly IPaymentService _paymentSvc;

        public PaymentsController(AppDbContext db, IHostEnvironment env, IConfiguration cfg, IWspayClient wspClient, IPaymentService paymentSvc)
        {
            _db = db; _env = env; _cfg = cfg; _wspClient = wspClient; _paymentSvc = paymentSvc;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                var payload = await Request.ReadFromJsonAsync<Dictionary<string, object?>>();
                if (payload == null) return StatusCode(StatusCodes.Status400BadRequest);
                var status = (payload.TryGetValue("status", out var s) ? (s?.ToString() ?? "") : "").Trim().ToLowerInvariant();
                if (status != "success") return Ok(new { ignored = true });
                if (!payload.TryGetValue("contractId", out var cidObj)) return StatusCode(StatusCodes.Status400BadRequest);
                if (!int.TryParse(cidObj?.ToString(), out var contractId) || contractId <= 0) return StatusCode(StatusCodes.Status400BadRequest);

                var contract = await _db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && !c.IsDeleted);
                if (contract == null) return StatusCode(StatusCodes.Status404NotFound);
                if (contract.AmtMatched < contract.Amount)
                {
                    contract.AmtMatched = contract.Amount;
                    contract.LastUpdatedDt = DateTime.Now;
                    contract.Stamp = contract.Stamp + 1;
                    await _db.SaveChangesAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Information, "Payment matched", $"ContractId={contractId}; AmtMatched={contract.AmtMatched}", 2);
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Payment webhook failed", ex.ToString(), 2);
                return StatusCode(500);
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> Status([FromQuery] string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return JsonResultHelper.StableJson(_env, new { status = "Unknown" });
            var p = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.ExternalOrderId == reference);
            if (p == null) return JsonResultHelper.StableJson(_env, new { status = "Unknown" });
            return JsonResultHelper.StableJson(_env, new { status = p.Status, contractId = p.ContractId });
        }

        [HttpPost("wspay/callback")]
        public async Task<IActionResult> WspayCallback()
        {
            try
            {
                IDictionary<string, string> fields;
                if (Request.HasJsonContentType())
                {
                    var payload = await Request.ReadFromJsonAsync<Dictionary<string, object?>>();
                    if (payload == null) return StatusCode(StatusCodes.Status400BadRequest);
                    fields = payload.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    var form = await Request.ReadFormAsync();
                    fields = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                }

                var paymentsEnabled = string.Equals(_cfg["Payments:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
                var wspEnv = _cfg["Payments:WSPay:Environment"] ?? "Sandbox";
                var sig = fields.TryGetValue("signature", out var sv) ? sv : string.Empty;
                var secret = _cfg["Payments:WSPay:WebhookSecret"] ?? string.Empty;
                var fieldsNullable = fields.ToDictionary(kv => kv.Key, kv => (string?)kv.Value, StringComparer.OrdinalIgnoreCase);
                var canonical = _wspClient.BuildCanonicalStringForWebhook(fieldsNullable);

                if (!_env.IsEnvironment("Testing") && paymentsEnabled && !string.Equals(wspEnv, "Testing", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(sig) || string.IsNullOrEmpty(secret) || !_wspClient.VerifyWebhookSignature(sig, canonical, secret))
                        return StatusCode(StatusCodes.Status400BadRequest);
                }
                else
                {
                    if (!string.IsNullOrEmpty(sig) && !string.IsNullOrEmpty(secret))
                    {
                        var okSig = _wspClient.VerifyWebhookSignature(sig, canonical, secret);
                        if (!okSig) return StatusCode(StatusCodes.Status400BadRequest);
                    }
                }

                var result = await _paymentSvc.HandleCallbackAsync(fields);
                if (result.Success)
                {
                    if (result.Idempotent)
                        return JsonResultHelper.StableJson(_env, new { success = true, idempotent = true });
                    return JsonResultHelper.StableJson(_env, new { success = true });
                }
                else
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "WSPay payment failed", fields.TryGetValue("external_order_id", out var eo) ? $"ExternalOrderId={eo}" : string.Empty, 2);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = result.Message ?? "Payment failed. Please try again." });
                }
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "WSPay callback failed", ex.ToString(), 2);
                return StatusCode(500);
            }
        }

        [HttpPost("wspay/mock-callback")]
        public async Task<IActionResult> WspayMockCallback()
        {
            try
            {
                var env = _cfg["Payments:WSPay:Environment"] ?? (_env.IsProduction() ? "Production" : "Testing");
                if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(StatusCodes.Status403Forbidden);

                IDictionary<string, string> fields;
                if (Request.HasJsonContentType())
                {
                    var payload = await Request.ReadFromJsonAsync<Dictionary<string, object?>>();
                    if (payload == null) return StatusCode(StatusCodes.Status400BadRequest);
                    fields = payload.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    var form = await Request.ReadFormAsync();
                    fields = form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                }

                var statusRaw = fields.TryGetValue("status", out var st) ? (st ?? "").Trim().ToLowerInvariant() : string.Empty;
                if (statusRaw == "succeeded") fields["status"] = "success";
                else if (statusRaw == "failed" || statusRaw == "failure") fields["status"] = "failure";
                else if (statusRaw == "canceled" || statusRaw == "cancelled") fields["status"] = "failure";

                if (fields.TryGetValue("reference", out var refId) && !string.IsNullOrWhiteSpace(refId))
                    fields["external_order_id"] = refId;

                var result = await _paymentSvc.HandleCallbackAsync(fields);
                if (result.Success)
                    return JsonResultHelper.StableJson(_env, new { success = true });
                return JsonResultHelper.StableJson(_env, new { success = false, message = result.Message ?? "Payment failed. Please try again." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Mock WSPay callback failed", ex.ToString(), 2);
                return StatusCode(500);
            }
        }
    }
}