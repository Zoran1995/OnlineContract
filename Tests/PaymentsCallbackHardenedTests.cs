using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests
{
    public class PaymentsCallbackHardenedTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public PaymentsCallbackHardenedTests(WebAppFactory factory) { _factory = factory; }

        [Fact]
        public async System.Threading.Tasks.Task Duplicate_Success_Callback_Is_Idempotent()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 880, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = System.DateTime.Now, Amount = 100m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 881, ContractId = 880, ProductVariantId = 101, Quantity = 1, Amount = 100m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = System.DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            await db.SaveChangesAsync();

            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("user","Password1");
            var cb1 = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "success", contractId = 880 });
            var j1 = await cb1.Content.ReadFromJsonAsync<CallbackResult>();
            Assert.True(j1!.success);
            var cb2 = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "success", contractId = 880 });
            var j2 = await cb2.Content.ReadFromJsonAsync<CallbackResult2>();
            Assert.True(j2!.success);
            Assert.True(j2!.idempotent);
        }

        [Fact]
        public async System.Threading.Tasks.Task Mismatch_Amount_Fails_And_Stays_Draft()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contract = new Contract { Id = 890, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = System.DateTime.Now, Amount = 120m, IsActive = true, IsDeleted = false, Stamp = 0 };
            db.Contracts.Add(contract);
            var det = new ContractDet { Id = 891, ContractId = 890, ProductVariantId = 101, Quantity = 1, Amount = 120m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = System.DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
            db.ContractDets.Add(det);
            db.Payments.Add(new Payment{ PaymentId = 2001, ContractId = 890, Provider = "WSPay", ExternalOrderId = "C-890-ref", AmountGross = 120m, Currency = "RSD", Status = "Pending", CreatedDt = System.DateTime.Now });
            await db.SaveChangesAsync();

            var (client, _) = await _factory.CreateAuthenticatedClientAsync("user","Password1");
            var cb = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "success", contractId = 890, external_order_id = "C-890-ref", amount = "999.99", currency = "RSD" });
            var j = await cb.Content.ReadFromJsonAsync<CallbackResult>();
            Assert.False(j!.success);

            using var scope2 = _factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db2.Contracts.AsNoTracking().SingleAsync(x => x.Id == 890);
            Assert.Equal(OnlineContract.Helpers.ContractState.Draft, c.ContractState);
        }
    }

    file sealed class CallbackResult { public bool success { get; set; } }
    file sealed class CallbackResult2 { public bool success { get; set; } public bool idempotent { get; set; } }
}