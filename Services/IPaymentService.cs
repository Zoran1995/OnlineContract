using System.Threading.Tasks;
using OnlineContract.Models;

namespace OnlineContract.Services
{
    public record CustomerInfo(string Email, string FullName);
    public record PaymentCallbackResult(bool Success, bool Idempotent, string? Message);

    public interface IPaymentService
    {
        Task<(bool ok, string redirectUrl, string externalOrderId, string? error)> CreatePaymentIntentAsync(int contractId, decimal amount, CustomerInfo info);
        Task<PaymentCallbackResult> HandleCallbackAsync(IDictionary<string, string> fields);
    }
}